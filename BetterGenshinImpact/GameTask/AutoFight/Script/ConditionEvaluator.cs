using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using AutoFightSkill = BetterGenshinImpact.GameTask.AutoFight.AutoFightSkill;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

/// <summary>
/// 条件表达式求值器
/// 支持语法：||, &&, !, (), +, -, *, /, >, <, =, 函数调用
/// 支持函数：last-exec, q-ready, e-ready, e-cd, low-hp, battle-time, in-party, onfield, t, since, count, min, max, last-check
/// </summary>
public class ConditionEvaluator
{
    /// <summary>内置条件函数名（词法解析时优先按函数名合并连字符）</summary>
    public static readonly HashSet<string> FunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "last-exec", "q-ready", "e-ready", "e-cd", "low-hp", "battle-time", "in-party", "onfield", "t", "since", "count", "min", "max", "last-check"
    };

    /// <summary>
    /// 校验动作名能否作为条件表达式中的单个标识符解析：
    /// 将动作名置于"全部动作名 + 内置函数名"的已知表内做词法解析，要求恰好解析为一个标识符。
    /// 拒绝布尔字面量（true/false，不区分大小写）、纯数字、含空白/逗号/运算符等无法作为动作标识符的名称，以及内置函数名。
    /// </summary>
    public static bool IsValidActionName(string name, IEnumerable<string> allActionNames)
    {
        if (string.IsNullOrEmpty(name) || bool.TryParse(name, out _)) return false;
        if (FunctionNames.Contains(name)) return false;

        var known = new HashSet<string>(FunctionNames, StringComparer.OrdinalIgnoreCase);
        foreach (var n in allActionNames) known.Add(n);

        List<Token> tokens;
        try
        {
            tokens = Tokenize(name, known);
        }
        catch (InvalidOperationException)
        {
            return false; // 含无法识别的字符
        }

        // 期望恰好一个 Identifier token，且与动作名一致，其余仅有 End
        return tokens.Count == 2
               && tokens[0].Type == TokenType.Identifier
               && string.Equals(tokens[0].Value, name, StringComparison.OrdinalIgnoreCase);
    }

    // 动作执行事件记录：序号、名称、距离开战的相对时间（秒），供 since/count 等按序号或名称查询
    private readonly List<(int Index, string Name, double Time)> _execHistory = new();
    private readonly DateTime _battleStartTime;
    private readonly CombatScenes _combatScenes;
    private readonly Func<ImageRegion> _captureFunc;
    // 策略中声明的动作名（词法解析时用于连字符合并判断）
    private readonly HashSet<string> _actionNames;
    private HashSet<string>? _knownIdentifiers;
    private ImageRegion? _cachedCapture;
    private string? _currentCharacterName;
    private string? _currentActionName;
    private HashSet<int>? _qReadyCache;
    private bool? _lowHpCache;
    // 出战角色识别上下文：箭头识别需在同一 context 内累计两次相同结果才返回有效编号，
    // 跨轮复用避免每次 onfield() 求值都从零统计导致永远识别失败
    private readonly AvatarActiveCheckContext _avatarActiveCheckContext = new();

    public ConditionEvaluator(CombatScenes combatScenes, Func<ImageRegion> captureFunc, IEnumerable<string>? actionNames = null)
    {
        _battleStartTime = DateTime.Now;
        _combatScenes = combatScenes;
        _captureFunc = captureFunc;
        _actionNames = actionNames != null
            ? new HashSet<string>(actionNames, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>词法解析时的已知标识符集合：内置函数名 + 策略动作名（首次使用时构建）</summary>
    private HashSet<string> GetKnownIdentifiers()
    {
        if (_knownIdentifiers == null)
        {
            _knownIdentifiers = new HashSet<string>(FunctionNames, StringComparer.OrdinalIgnoreCase);
            foreach (var name in _actionNames) _knownIdentifiers.Add(name);
        }
        return _knownIdentifiers;
    }

    /// <summary>
    /// 设置缓存截图，供本次循环中的条件求值复用（q-ready, low-hp 等）。
    /// 每次循环开始时截取一次并传入，避免多次截图带来的性能开销。
    /// </summary>
    public void SetCachedCapture(ImageRegion? capture)
    {
        _cachedCapture = capture;
        _qReadyCache = null;
        _lowHpCache = null;
    }

    /// <summary>
    /// 获取截图：优先使用缓存截图，否则新建截图
    /// </summary>
    private ImageRegion GetCapture()
    {
        return _cachedCapture ?? _captureFunc();
    }

    /// <summary>
    /// 更新动作的最后执行时间，并记录一条执行事件（序号 + 名称 + 距离开战相对时间）。
    /// since/count 等查询基于该事件记录。
    /// </summary>
    /// <param name="index">动作序号</param>
    /// <param name="name">动作名称</param>
    public void UpdateLastExecTime(int index, string name)
    {
        var now = DateTime.Now;
        _execHistory.Add((index, name, (now - _battleStartTime).TotalSeconds));
    }

    /// <summary>
    /// 求值条件表达式
    /// </summary>
    /// <param name="expression">表达式字符串</param>
    /// <param name="currentIndex">当前动作索引</param>
    /// <param name="characterName">当前角色名称</param>
    /// <param name="actionName">当前动作名称（since/count/last-exec 缺省严格指代当前动作时使用）</param>
    /// <returns>表达式结果</returns>
    public bool Evaluate(string expression, int currentIndex, string? characterName = null, string? actionName = null)
    {
        _currentCharacterName = characterName;
        _currentActionName = actionName;
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        try
        {
            var tokens = Tokenize(expression, GetKnownIdentifiers());
            var pos = 0;
            var ast = ParseOrExpr(tokens, ref pos);
            return ToBool(Eval(ast, currentIndex));
        }
        catch (Exception e)
        {
            Logger.LogWarning("条件表达式求值失败：{Expr}，{Msg}", expression, e.Message);
            return false;
        }
    }

    // ========== 词法分析 ==========

    private enum TokenType { Identifier, Number, Bool, And, Or, Not, Plus, Minus, Mul, Div, Greater, Less, Equal, LParen, RParen, Comma, End }

    private readonly struct Token(TokenType type, string value)
    {
        public TokenType Type { get; } = type;
        public string Value { get; } = value;
    }

    private static List<Token> Tokenize(string expr, HashSet<string> knownIdentifiers)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < expr.Length)
        {
            if (char.IsWhiteSpace(expr[i])) { i++; continue; }

            if (i + 1 < expr.Length)
            {
                if (expr[i] == '&' && expr[i + 1] == '&') { tokens.Add(new Token(TokenType.And, "&&")); i += 2; continue; }
                if (expr[i] == '|' && expr[i + 1] == '|') { tokens.Add(new Token(TokenType.Or, "||")); i += 2; continue; }
            }

            var c = expr[i];
            if (c == '(') { tokens.Add(new Token(TokenType.LParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenType.RParen, ")")); i++; continue; }
            if (c == ',') { tokens.Add(new Token(TokenType.Comma, ",")); i++; continue; }
            if (c == '!') { tokens.Add(new Token(TokenType.Not, "!")); i++; continue; }
            if (c == '+') { tokens.Add(new Token(TokenType.Plus, "+")); i++; continue; }
            if (c == '-') { tokens.Add(new Token(TokenType.Minus, "-")); i++; continue; }
            if (c == '*') { tokens.Add(new Token(TokenType.Mul, "*")); i++; continue; }
            if (c == '/') { tokens.Add(new Token(TokenType.Div, "/")); i++; continue; }
            if (c == '>') { tokens.Add(new Token(TokenType.Greater, ">")); i++; continue; }
            if (c == '<') { tokens.Add(new Token(TokenType.Less, "<")); i++; continue; }
            if (c == '=') { tokens.Add(new Token(TokenType.Equal, "=")); i++; continue; }

            if (char.IsDigit(c) || (c == '.' && i + 1 < expr.Length && char.IsDigit(expr[i + 1])))
            {
                var start = i;
                while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.')) i++;
                tokens.Add(new Token(TokenType.Number, expr[start..i]));
                continue;
            }

            if (char.IsLetter(c))
            {
                var start = i;
                // 先读取基础字母/数字段（中文名、角色名、函数名首段）
                while (i < expr.Length && char.IsLetterOrDigit(expr[i])) i++;
                // 连字符合并：先扫描出基础段之后最长的"字母数字+连字符"候选，再回退到最长的已知标识符。
                // 多段动作名（如 芙芙-e-开场）只需其完整名称已声明即可整体并入，中间段无需单独声明；
                // 没有任何已知前缀时 `-` 保持为独立减号运算符（如 t-5、since(1)-3）
                if (i < expr.Length && expr[i] == '-')
                {
                    var candidateEnd = i + 1;
                    while (candidateEnd < expr.Length && (char.IsLetterOrDigit(expr[candidateEnd]) || expr[candidateEnd] == '-'))
                        candidateEnd++;
                    while (candidateEnd > i && !knownIdentifiers.Contains(expr[start..candidateEnd]))
                        candidateEnd--;
                    if (candidateEnd > i) i = candidateEnd;
                }
                var word = expr[start..i];
                tokens.Add(word is "true" or "false"
                    ? new Token(TokenType.Bool, word)
                    : new Token(TokenType.Identifier, word));
                continue;
            }

            throw new InvalidOperationException($"无法识别的字符：'{c}'");
        }

        tokens.Add(new Token(TokenType.End, ""));
        return tokens;
    }

    // ========== 语法分析（递归下降） ==========

    private abstract class AstNode { }

    private class BoolNode(bool value) : AstNode { public bool Value { get; } = value; }

    private class NumberNode(double value) : AstNode { public double Value { get; } = value; }

    private class FuncCallNode(string name, List<AstNode> args) : AstNode
    {
        public string Name { get; } = name;
        public List<AstNode> Args { get; } = args;
    }

    private class UnaryOpNode(string op, AstNode operand) : AstNode
    {
        public string Op { get; } = op;
        public AstNode Operand { get; } = operand;
    }

    private class BinaryOpNode(string op, AstNode left, AstNode right) : AstNode
    {
        public string Op { get; } = op;
        public AstNode Left { get; } = left;
        public AstNode Right { get; } = right;
    }

    // 优先级：|| < && < 比较 < +- < */ < 一元 < 基本

    private static AstNode ParseOrExpr(List<Token> tokens, ref int pos)
    {
        var left = ParseAndExpr(tokens, ref pos);
        while (tokens[pos].Type == TokenType.Or)
        {
            var op = tokens[pos].Value; pos++;
            var right = ParseAndExpr(tokens, ref pos);
            left = new BinaryOpNode(op, left, right);
        }
        return left;
    }

    private static AstNode ParseAndExpr(List<Token> tokens, ref int pos)
    {
        var left = ParseCompareExpr(tokens, ref pos);
        while (tokens[pos].Type == TokenType.And)
        {
            var op = tokens[pos].Value; pos++;
            var right = ParseCompareExpr(tokens, ref pos);
            left = new BinaryOpNode(op, left, right);
        }
        return left;
    }

    private static AstNode ParseCompareExpr(List<Token> tokens, ref int pos)
    {
        var left = ParseAddExpr(tokens, ref pos);
        while (tokens[pos].Type is TokenType.Greater or TokenType.Less or TokenType.Equal)
        {
            var op = tokens[pos].Value; pos++;
            var right = ParseAddExpr(tokens, ref pos);
            left = new BinaryOpNode(op, left, right);
        }
        return left;
    }

    private static AstNode ParseAddExpr(List<Token> tokens, ref int pos)
    {
        var left = ParseMulExpr(tokens, ref pos);
        while (tokens[pos].Type is TokenType.Plus or TokenType.Minus)
        {
            var op = tokens[pos].Value; pos++;
            var right = ParseMulExpr(tokens, ref pos);
            left = new BinaryOpNode(op, left, right);
        }
        return left;
    }

    private static AstNode ParseMulExpr(List<Token> tokens, ref int pos)
    {
        var left = ParseUnaryExpr(tokens, ref pos);
        while (tokens[pos].Type is TokenType.Mul or TokenType.Div)
        {
            var op = tokens[pos].Value; pos++;
            var right = ParseUnaryExpr(tokens, ref pos);
            left = new BinaryOpNode(op, left, right);
        }
        return left;
    }

    private static AstNode ParseUnaryExpr(List<Token> tokens, ref int pos)
    {
        if (tokens[pos].Type == TokenType.Not)
        {
            var op = tokens[pos].Value; pos++;
            return new UnaryOpNode(op, ParseUnaryExpr(tokens, ref pos));
        }
        if (tokens[pos].Type == TokenType.Minus)
        {
            pos++;
            return new UnaryOpNode("-u", ParseUnaryExpr(tokens, ref pos));
        }
        return ParsePrimary(tokens, ref pos);
    }

    private static AstNode ParsePrimary(List<Token> tokens, ref int pos)
    {
        if (tokens[pos].Type == TokenType.LParen)
        {
            pos++;
            var node = ParseOrExpr(tokens, ref pos);
            if (tokens[pos].Type != TokenType.RParen) throw new InvalidOperationException("缺少右括号");
            pos++;
            return node;
        }

        if (tokens[pos].Type == TokenType.Identifier)
        {
            var name = tokens[pos].Value; pos++;
            if (tokens[pos].Type == TokenType.LParen)
            {
                pos++;
                var args = new List<AstNode>();
                if (tokens[pos].Type != TokenType.RParen)
                {
                    args.Add(ParseOrExpr(tokens, ref pos));
                    while (tokens[pos].Type == TokenType.Comma)
                    {
                        pos++;
                        args.Add(ParseOrExpr(tokens, ref pos));
                    }
                }
                if (tokens[pos].Type != TokenType.RParen) throw new InvalidOperationException($"函数 {name} 缺少右括号");
                pos++;
                return new FuncCallNode(name, args);
            }
            return new FuncCallNode(name, []);
        }

        if (tokens[pos].Type == TokenType.Number)
        {
            var val = double.Parse(tokens[pos].Value, CultureInfo.InvariantCulture); pos++;
            return new NumberNode(val);
        }

        if (tokens[pos].Type == TokenType.Bool)
        {
            var val = tokens[pos].Value == "true"; pos++;
            return new BoolNode(val);
        }

        throw new InvalidOperationException($"意外的 token：{tokens[pos].Value}");
    }

    // ========== AST 求值（统一返回 object: double 或 bool） ==========

    /// <summary>求值 AST 节点</summary>
    private object Eval(AstNode node, int currentIndex)
    {
        return node switch
        {
            BoolNode b => b.Value,
            NumberNode n => n.Value,
            UnaryOpNode u => EvalUnary(u, currentIndex),
            BinaryOpNode b => EvalBinary(b, currentIndex),
            FuncCallNode f => EvalFunc(f.Name, f.Args, currentIndex),
            _ => false
        };
    }

    /// <summary>求值二元运算节点</summary>
    private object EvalBinary(BinaryOpNode node, int currentIndex)
    {
        var left = Eval(node.Left, currentIndex);

        // 短路求值
        if (node.Op == "&&") return ToBool(left) && ToBool(Eval(node.Right, currentIndex));
        if (node.Op == "||") return ToBool(left) || ToBool(Eval(node.Right, currentIndex));

        var right = Eval(node.Right, currentIndex);
        return node.Op switch
        {
            ">" => ToNumber(left) > ToNumber(right),
            "<" => ToNumber(left) < ToNumber(right),
            "=" => Math.Abs(ToNumber(left) - ToNumber(right)) < 0.0001, // 浮点数相等比较
            "+" => ToNumber(left) + ToNumber(right),
            "-" => ToNumber(left) - ToNumber(right),
            "*" => ToNumber(left) * ToNumber(right),
            "/" => ToNumber(right) != 0 ? ToNumber(left) / ToNumber(right) : 0,
            _ => false
        };
    }

    /// <summary>求值一元运算节点</summary>
    private object EvalUnary(UnaryOpNode node, int currentIndex)
    {
        var operand = Eval(node.Operand, currentIndex);
        return node.Op switch
        {
            "!" => !ToBool(operand),
            "-u" => -ToNumber(operand),
            _ => false
        };
    }

    /// <summary>求值函数调用节点</summary>
    private object EvalFunc(string name, List<AstNode> args, int currentIndex)
    {
        // 函数名大小写不敏感（min/MIN、MAX/max 均可）
        name = name.ToLowerInvariant();
        return name switch
        {
            "last-exec" => EvalLastExec(args, currentIndex),
            "q-ready" => EvalQReady(args),
            "e-ready" => EvalEReady(args),
            "e-cd" => EvalECd(args),
            "low-hp" => EvalLowHp(),
            "battle-time" => EvalBattleTime(args),
            "in-party" => EvalInParty(args),
            "onfield" => EvalOnField(),
            "t" => EvalT(),
            "since" => EvalSince(args, currentIndex),
            "count" => EvalCount(args, currentIndex),
            "min" => EvalMinMax(args, currentIndex, isMax: false),
            "max" => EvalMinMax(args, currentIndex, isMax: true),
            "last-check" => EvalLastCheck(),
            _ => throw new InvalidOperationException($"未知条件函数：{name}")
        };
    }

    // ========== 类型转换 ==========

    /// <summary>将对象转换为 bool</summary>
    private static bool ToBool(object val)
    {
        return val switch
        {
            bool b => b,
            double d => d > 0,
            _ => false
        };
    }

    /// <summary>将对象转换为 double</summary>
    private static double ToNumber(object val)
    {
        return val switch
        {
            double d => d,
            bool b => b ? 1 : 0,
            _ => 0
        };
    }

    /// <summary>求值并转换为 double</summary>
    private double EvalNumber(AstNode node, int currentIndex)
    {
        return ToNumber(Eval(node, currentIndex));
    }

    /// <summary>
    /// 目标动作标识：按序号（Index）和/或动作名称（Name）解析。
    /// 缺省指代当前动作时为严格指代（Index 与 Name 同时给出，两者都必须匹配）。
    /// </summary>
    private readonly record struct TargetRef(int? Index, string? Name);

    /// <summary>
    /// 解析目标动作标识：纯数字参数按序号解析；裸名称（如 since(动作名)）按动作名称解析；
    /// 其他表达式（如 since(1+1)）求值后按序号解析。
    /// </summary>
    private TargetRef ResolveTarget(AstNode node, int currentIndex)
    {
        if (node is NumberNode n)
            return new TargetRef((int)n.Value, null);

        if (node is FuncCallNode f && f.Args.Count == 0)
        {
            // 裸名称必须已声明为策略动作名：拼错/未知名称直接报错（Evaluate 捕获后返回 false），
            // 否则会被当作"从未执行"处理，since(拼错名)>N 与 last-exec(N,true,拼错名) 会在战斗开始就为 true
            if (!_actionNames.Contains(f.Name))
                throw new InvalidOperationException($"未知动作名称：{f.Name}，按名称查询必须使用策略中已声明的动作名");
            return new TargetRef(null, f.Name);
        }

        return new TargetRef((int)ToNumber(Eval(node, currentIndex)), null);
    }

    /// <summary>
    /// 事件是否匹配目标标识：仅按序号、仅按名称、或序号与名称同时指定（严格指代，两者都必须匹配）。
    /// </summary>
    private static bool MatchesTarget((int Index, string Name, double Time) e, TargetRef target)
    {
        if (target.Name is not null && target.Index.HasValue)
            return e.Index == target.Index.Value && string.Equals(e.Name, target.Name, StringComparison.OrdinalIgnoreCase);
        if (target.Name is not null)
            return string.Equals(e.Name, target.Name, StringComparison.OrdinalIgnoreCase);
        return target.Index == e.Index;
    }

    /// <summary>
    /// 获取目标动作距上次执行的时间（秒）：
    /// 反向查找事件历史中最新一条符合目标标识的事件。从未执行返回 null。
    /// </summary>
    private double? GetLastExecElapsed(TargetRef target)
    {
        var currentT = (DateTime.Now - _battleStartTime).TotalSeconds;
        for (var i = _execHistory.Count - 1; i >= 0; i--)
        {
            if (MatchesTarget(_execHistory[i], target))
                return currentT - _execHistory[i].Time;
        }
        return null;
    }

    // ========== 布尔函数（返回 bool） ==========

    /// <summary>
    /// 判断动作上次执行距离现在是否超过/少于指定时间。
    /// 目标动作支持序号或动作名称（如 last-exec(2,true,3)、last-exec(2,true,香菱)），缺省指代当前动作。
    /// </summary>
    private bool EvalLastExec(List<AstNode> args, int currentIndex)
    {
        if (args.Count < 1) return false;

        var timeSec = EvalNumber(args[0], currentIndex);
        var greater = args.Count >= 2 && args[1] is BoolNode b ? b.Value : true;
        var target = args.Count >= 3 ? ResolveTarget(args[2], currentIndex) : new TargetRef(currentIndex, _currentActionName);

        var elapsed = GetLastExecElapsed(target);
        if (elapsed is null) return greater;

        return greater ? elapsed > timeSec : elapsed < timeSec;
    }

    /// <summary>
    /// 判断角色 Q 是否就绪。
    /// 每次主循环只检测一次，结果缓存供本循环内所有 q-ready 复用。
    /// 使用缓存截图进行全队 4 角色 Q 状态检测，避免重复截图。
    /// q-ready() 检查本动作所属角色；q-ready(角色名) 检查指定角色。
    /// 检测采用两路独立检测后合并：侧边栏检测后台角色 + 中央检测场上角色，OR 合并。
    /// </summary>
    private bool EvalQReady(List<AstNode> args)
    {
        string? targetName;
        if (args.Count >= 1 && args[0] is FuncCallNode f && f.Args.Count == 0)
            targetName = f.Name;
        else
            targetName = _currentCharacterName;

        try
        {
            if (_qReadyCache == null)
            {
                var capture = GetCapture();
                var ownCapture = _cachedCapture == null;
                try
                {
                    using var clonedMat = capture.SrcMat.Clone();
                    using var clone = new ImageRegion(clonedMat, 0, 0);

                    // ① 侧边栏检测：检测所有 4 个角色侧边栏 Q 图标（主要捕获后台角色）
                    var sidePanelReady = AutoFightSkill.AvatarQSkillAsync(clone).Result;

                    // ② 场上角色中央检测：仅检测当前场上角色的中央 Q 图标
                    var centerReady = new List<int>();
                    var currentOnFieldIndex = _combatScenes.LastActiveAvatarIndex;
                    if (currentOnFieldIndex > 0)
                    {
                        // 仅对场上角色单独检测中央 Q 区域
                        centerReady = AutoFightSkill.AvatarQSkillAsync(clone,
                            new List<int> { currentOnFieldIndex }, currentOnFieldIndex).Result;
                    }

                    // ③ 合并：OR 逻辑，只要有一路检测到就视为就绪
                    _qReadyCache = new HashSet<int>(sidePanelReady.Union(centerReady));
                }
                finally
                {
                    if (ownCapture) capture.Dispose();
                }
            }

            foreach (var avatar in _combatScenes.GetAvatars())
            {
                if (targetName != null && !avatar.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_qReadyCache.Contains(avatar.Index))
                {
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning("[Q检测] 异常：{Msg}", e.Message);
        }

        return false;
    }

    /// <summary>
    /// 判断指定角色 E 技能是否就绪。
    /// e-ready() 检查本动作所属角色；e-ready(角色名) 检查指定角色。
    /// 数据来源为 <see cref="ESkillCdTracker"/>（跨战斗持久化的 OCR 冷却记录）。
    /// </summary>
    private bool EvalEReady(List<AstNode> args)
    {
        string? targetName;
        if (args.Count >= 1 && args[0] is FuncCallNode f && f.Args.Count == 0)
            targetName = f.Name;
        else
            targetName = _currentCharacterName;

        if (targetName == null) return false;
        return ESkillCdTracker.IsReady(targetName);
    }

    /// <summary>
    /// 获取指定角色 E 技能的剩余冷却秒数。
    /// e-cd() 返回本动作所属角色的剩余 CD；e-cd(角色名) 返回指定角色的。
    /// 数据来源为 <see cref="ESkillCdTracker"/>（跨战斗持久化的 OCR 冷却记录）。
    /// 返回 0 表示就绪或无需冷却。
    /// </summary>
    private double EvalECd(List<AstNode> args)
    {
        string? targetName;
        if (args.Count >= 1 && args[0] is FuncCallNode f && f.Args.Count == 0)
            targetName = f.Name;
        else
            targetName = _currentCharacterName;

        if (targetName == null) return 0;
        return ESkillCdTracker.GetRemainingCd(targetName);
    }

    /// <summary>
    /// 判断当前角色是否低血量（使用缓存的截图，每轮循环只检测一次）
    /// </summary>
    private bool EvalLowHp()
    {
        if (_lowHpCache.HasValue)
            return _lowHpCache.Value;

        try
        {
            var ra = GetCapture();
            var ownRa = _cachedCapture == null;
            try
            {
                _lowHpCache = Bv.CurrentAvatarIsLowHp(ra);
                return _lowHpCache.Value;
            }
            finally
            {
                if (ownRa) ra.Dispose();
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning("[低血检测] 异常：{Msg}", e.Message);
            _lowHpCache = false;
            return false;
        }
    }

    /// <summary>
    /// 判断战斗持续时长（保留旧函数，单位为毫秒）
    /// </summary>
    private bool EvalBattleTime(List<AstNode> args)
    {
        // 使用 currentIndex=0 求值，因为 battle-time 参数不涉及动作索引
        if (args.Count < 1) return false;

        var timeSec = EvalNumber(args[0], 0);
        var greater = args.Count >= 2 && args[1] is BoolNode b ? b.Value : true;
        var elapsed = (DateTime.Now - _battleStartTime).TotalSeconds;
        return greater ? elapsed > timeSec : elapsed < timeSec;
    }

    /// <summary>
    /// 判断指定角色是否在当前队伍中
    /// </summary>
    private bool EvalInParty(List<AstNode> args)
    {
        if (args.Count < 1 || !(args[0] is FuncCallNode f) || f.Args.Count != 0)
            return false;

        var targetName = f.Name;
        return _combatScenes.SelectAvatar(targetName) != null;
    }

    /// <summary>
    /// 判断动作的归属角色是否正在场上。
    /// 动作无归属角色（Character 为空）、归属角色不在队伍中或不在场上时返回 false。
    /// </summary>
    private bool EvalOnField()
    {
        if (string.IsNullOrEmpty(_currentCharacterName)) return false;

        // 首次求值时 LastActiveAvatarIndex 尚未初始化（InitializeTeam 只识别队伍，不识别出战角色），
        // 直接用缓存截图刷新当前出战编号；仍识别失败才返回 false，避免 onfield() 开战第一轮误判
        if (_combatScenes.LastActiveAvatarIndex <= 0)
        {
            var capture = GetCapture();
            try
            {
                // 复用跨轮识别上下文：箭头识别需同一 context 累计两次相同结果才返回有效编号，
                // 新建 context 会导致每轮都从零统计、箭头识别永远返回 -2（重试）而无法刷新 LastActiveAvatarIndex
                if (_combatScenes.GetActiveAvatarIndex(capture, _avatarActiveCheckContext) <= 0) return false;
            }
            finally
            {
                // 仅释放自己新建的截图；缓存截图归调用方管理，不在此释放
                if (_cachedCapture == null) capture.Dispose();
            }
        }

        var avatar = _combatScenes.SelectAvatar(_currentCharacterName);
        return avatar != null && avatar.Index == _combatScenes.LastActiveAvatarIndex;
    }

    // ========== 数值函数（返回 double） ==========

    /// <summary>
    /// 距离开战至今的时间，单位秒
    /// </summary>
    private double EvalT()
    {
        return (DateTime.Now - _battleStartTime).TotalSeconds;
    }

    /// <summary>
    /// 距最近一次战斗结束检查的时间，单位秒（如 last-check() > 3）
    /// 数据源为 AutoFightTask.LastFightFinishCheckTime：由战斗结束检查与策略中的 check 指令更新，
    /// 战斗开始时在 AutoFightJsonTask 中重置，供 JSON 策略按检查间隔编排动作。
    /// </summary>
    private double EvalLastCheck()
    {
        return (DateTime.Now - AutoFightTask.LastFightFinishCheckTime).TotalSeconds;
    }

    /// <summary>
    /// 距离动作上次执行的时间，单位秒
    /// 不传参时严格指代当前动作（序号与名称都相同才算）；目标动作支持序号或动作名称（如 since(3)、since(香菱)）；从未执行返回正无穷
    /// </summary>
    private double EvalSince(List<AstNode> args, int currentIndex)
    {
        var target = args.Count >= 1 ? ResolveTarget(args[0], currentIndex) : new TargetRef(currentIndex, _currentActionName);

        var elapsed = GetLastExecElapsed(target);
        return elapsed ?? double.PositiveInfinity;
    }

    /// <summary>
    /// 动作在指定时间范围内的执行次数
    /// 目标动作不传时严格指代自己（序号与名称都相同才算），支持序号或动作名称（如 count(3)、count(香菱)）；start 默认为 0（战斗开始）；end 默认为当前时间 t
    /// </summary>
    private double EvalCount(List<AstNode> args, int currentIndex)
    {
        var currentT = (DateTime.Now - _battleStartTime).TotalSeconds;
        var target = args.Count >= 1 ? ResolveTarget(args[0], currentIndex) : new TargetRef(currentIndex, _currentActionName);
        var start = args.Count >= 2 ? ToNumber(Eval(args[1], currentIndex)) : 0;
        var end = args.Count >= 3 ? ToNumber(Eval(args[2], currentIndex)) : currentT;

        return _execHistory.Count(e => MatchesTarget(e, target) && e.Time >= start && e.Time <= end);
    }

    /// <summary>
    /// 返回内部以逗号分隔的各项表达式的最小值或最大值（如 min(since(), 5)、max(since(1), since(2))）。
    /// 各项求值后取数值；无参数时返回 0。
    /// </summary>
    private double EvalMinMax(List<AstNode> args, int currentIndex, bool isMax)
    {
        if (args.Count == 0) return 0;

        var result = isMax ? double.NegativeInfinity : double.PositiveInfinity;
        foreach (var arg in args)
        {
            var value = ToNumber(Eval(arg, currentIndex));
            result = isMax ? Math.Max(result, value) : Math.Min(result, value);
        }
        return result;
    }
}
