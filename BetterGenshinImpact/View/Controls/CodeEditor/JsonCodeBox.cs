using BetterGenshinImpact.Helpers;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.IO;
using System.Xml;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.View.Controls.CodeEditor;

public class JsonCodeBox : CodeBox
{
    public JsonCodeBox() : base()
    {
        RegisterHighlighting();
    }

    private void RegisterHighlighting()
    {
        IHighlightingDefinition luaHighlighting;
        using Stream s = File.OpenRead(Global.Absolute(@"Assets\Highlighting\Json.xshd"));
        using XmlReader reader = new XmlTextReader(s);
        luaHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);

        HighlightingManager.Instance.RegisterHighlighting("Json", [".json"], luaHighlighting);
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Json");
    }
}
