using System.IO;
using System.Xml;
using BetterGenshinImpact.Helpers;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

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
        using Stream s = ResourceHelper.GetStream(@"pack://application:,,,/Assets/Highlighting/Json.xshd");
        using XmlReader reader = new XmlTextReader(s);
        luaHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);

        HighlightingManager.Instance.RegisterHighlighting("Json", new string[] { ".json" }, luaHighlighting);
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Json");
    }
}
