import re

csp = "MyTodo/MyTodo.csproj"
cs = open(csp, encoding="utf-8-sig").read()
if "ApplicationIcon" not in cs:
    cs = cs.replace("  </PropertyGroup>", "    <ApplicationIcon>Assets\\app.ico</ApplicationIcon>\n  </PropertyGroup>")
if "EmbeddedResource" not in cs:
    cs = cs.replace("</Project>", "  <ItemGroup>\n    <EmbeddedResource Include=\"Assets\\tray.ico\" />\n  </ItemGroup>\n\n</Project>")
open(csp, "w", encoding="utf-8-sig", newline="").write(cs)

mw = "MyTodo/MainWindow.xaml.cs"
src = open(mw, encoding="utf-8-sig").read()
new_method = (
    "    private Icon MakeIcon()\n"
    "    {\n"
    "        var asm = typeof(MainWindow).Assembly;\n"
    "        using var stream = asm.GetManifestResourceStream(\"MyTodo.Assets.tray.ico\")\n"
    "            ?? throw new InvalidOperationException(\"Embedded resource MyTodo.Assets.tray.ico not found.\");\n"
    "        return new Icon(stream);\n"
    "    }"
)
pat = re.compile(r"private Icon MakeIcon\(\)[\s\S]*?\r?\n    \}")
src2, n = pat.subn(lambda m: new_method, src, count=1)
if n != 1:
    raise SystemExit("MakeIcon method not found")
open(mw, "w", encoding="utf-8-sig", newline="").write(src2)
print("patched csproj + MainWindow.xaml.cs")
