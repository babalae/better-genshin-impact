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
/// 支持函数：last-exec, q-ready, low-hp, battle-time, in-party, t, since, count
/// </summary>
public class ConditionEvaluator
{
    private readonly Dictionary<int, DateTime> _lastExecTimes = new();
    private readonly List<(int Index, double Time)> _execHistory = new();
    private readonly DateTime _battleStartTime;
    private readonly CombatScenes _combatScenes;
    private readonly Func<ImageRegion> _captureFunc;
    private ImageRegion? _cachedCapture;
    private string? _currentCharacterName;
    private HashSet<int>? _qReadyCache;
    private bool? _lowHpCache;

    public ConditionEvaluator(CombatScenes combatScenes, Func<ImageRegion> captureFunc)
    {
        _battleStartTime = DateTime.Now;
        _combatScenes = combatScenes;
        _captureFunc = captureFunc;
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
    /// 更新动作的最后执行时间
    /// </summary>
    /// <param name="index">动作索引</param>
    public void UpdateLastExecTime(int index)
    {
        var now = DateTime.Now;
        _lastExecTimes[index] = now;
        _execHistory.Add((index, (now - _battleStartTime).TotalSeconds));
    }

    /// <summary>
    /// 求值条件表达式
    /// </summary>
    /// <param name="expression">表达式字符串</param>
    /// <param name="currentIndex">当前动作索引</param>
    /// <param name="characterName">当前角色名称</param>
    /// <returns>表达式结果</returns>
    public bool Evaluate(string expression, int currentIndex, string? characterName = null)
    {
        _currentCharacterName = characterName;
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        try
        {
            var tokens = Tokenize(expression);
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

    private static List<Token> Tokenize(string expr)
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

            if (char.IsLetter(c) || c == '-')
            {
                var start = i;
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || (expr[i] == '-' && i + 1 < expr.Length && char.IsLetter(expr[i + 1])))) i++;
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
        return name switch
        {
            "last-exec" => EvalLastExec(args, currentIndex),
            "q-ready" => EvalQReady(args),
            "e-ready" => EvalEReady(args),
            "e-cd" => EvalECd(args),
            "low-hp" => EvalLowHp(),
            "battle-time" => EvalBattleTime(args),
            "in-party" => EvalInParty(args),
            "t" => EvalT(),
            "since" => EvalSince(args, currentIndex),
            "count" => EvalCount(args, currentIndex),
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

    // ========== 布尔函数（返回 bool） ==========

    /// <summary>判断动作上次执行距离现在是否超过/少于指定时间</summary>
    private bool EvalLastExec(List<AstNode> args, int currentIndex)
    {
        if (args.Count < 1) return false;

        var timeSec = EvalNumber(args[0], currentIndex);
        var greater = args.Count >= 2 && args[1] is BoolNode b ? b.Value : true;
        var targetIndex = args.Count >= 3 ? (int)EvalNumber(args[2], currentIndex) : currentIndex;

        if (!_lastExecTimes.TryGetValue(targetIndex, out var lastTime))
            return greater;

        var elapsed = (DateTime.Now - lastTime).TotalSeconds;
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

    // ========== 数值函数（返回 double） ==========

    /// <summary>
    /// 距离开战至今的时间，单位秒
    /// </summary>
    private double EvalT()
    {
        return (DateTime.Now - _battleStartTime).TotalSeconds;
    }

    /// <summary>
    /// 距离动作上次执行的时间，单位秒
    /// 不传参时指代当前动作；从未执行返回正无穷
    /// </summary>
    private double EvalSince(List<AstNode> args, int currentIndex)
    {
        var targetIndex = args.Count >= 1 ? (int)ToNumber(Eval(args[0], currentIndex)) : currentIndex;

        if (!_lastExecTimes.TryGetValue(targetIndex, out var lastTime))
            return double.PositiveInfinity;

        return (DateTime.Now - lastTime).TotalSeconds;
    }

    /// <summary>
    /// 动作在指定时间范围内的执行次数
    /// index 不传时指代自己；start 默认为 0（战斗开始）；end 默认为当前时间 t
    /// </summary>
    private double EvalCount(List<AstNode> args, int currentIndex)
    {
        var currentT = (DateTime.Now - _battleStartTime).TotalSeconds;
        var targetIndex = args.Count >= 1 ? (int)ToNumber(Eval(args[0], currentIndex)) : currentIndex;
        var start = args.Count >= 2 ? ToNumber(Eval(args[1], currentIndex)) : 0;
        var end = args.Count >= 3 ? ToNumber(Eval(args[2], currentIndex)) : currentT;

        return _execHistory.Count(e => e.Index == targetIndex && e.Time >= start && e.Time <= end);
    }
}
