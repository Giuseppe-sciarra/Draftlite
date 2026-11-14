using DraftLite.IO;
using DraftLite.Model;
using System.Globalization;

var folder = Path.Combine(Path.GetTempPath(), "DraftLite-tests-" + Guid.NewGuid());
Directory.CreateDirectory(folder);
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
void Near(double expected, double actual, string name) => Check(Math.Abs(expected - actual) < 0.001, name);
Screenplay Read(string xml)
{
    var path = Path.Combine(folder, "source.fdx");
    File.WriteAllText(path, xml);
    return FdxIO.Load(path);
}
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
    var sp = Read("""
        <FinalDraft>
          <Content><Paragraph Type="Action"><Text>Una stanza.</Text></Paragraph></Content>
          <PageLayout TopMargin="90" BottomMargin="54"><PageSize Width="8.5" Height="11"/></PageLayout>
          <ElementSettings Type="Action"><ParagraphSpec LeftIndent="1.25" RightIndent="7.25"/></ElementSettings>
          <ElementSettings Type="Character"><ParagraphSpec LeftIndent="3.75" RightIndent="7.25"/></ElementSettings>
          <ElementSettings Type="Dialogue"><ParagraphSpec LeftIndent="2.56" RightIndent="6.25"/></ElementSettings>
          <ElementSettings Type="Transition"><ParagraphSpec LeftIndent="5.25" RightIndent="6.75"/></ElementSettings>
        </FinalDraft>
        """);
    Near(8.5, sp.Layout.PaperWidth, "Larghezza pagina in pollici");
    Near(1.25, sp.Layout.TopMargin, "Margine verticale in punti convertito");
    Near(6, sp.Layout.TextWidth, "Area scrivibile dal modello");
    Near(1.31, sp.Layout.Style(ElementType.Dialogue).LeftInch, "Rientro dialogo relativo al testo");
    Near(3.69, sp.Layout.Style(ElementType.Dialogue).WidthInch, "RightIndent come coordinata");
    Near(0, sp.Layout.Style(ElementType.Character).RightTwips, "Rientro destro personaggio");
    var fdx = Path.Combine(folder, "roundtrip.fdx");
    FdxIO.Save(fdx, sp);
    var round = FdxIO.Load(fdx);
    Near(3.69, round.Layout.Style(ElementType.Dialogue).WidthInch, "Round trip FDX con cultura italiana");
    var native = Path.Combine(folder, "roundtrip.dlite");
    DraftLiteFile.Save(native, sp);
    Near(1.25, DraftLiteFile.Load(native).Layout.LeftMargin, "Round trip nativo");
    var clone = sp.Clone();
    clone.Layout.Paragraphs[ElementType.Action].Left = 2;
    Near(1.25, sp.Layout.Paragraphs[ElementType.Action].Left, "Clone indipendente");
    File.WriteAllText(native, "{\"Version\":2,\"Elements\":[]}");
    Check(DraftLiteFile.Load(native).Layout == null, "Compatibilita documenti versione 2");
    var plain = Read("<FinalDraft><Content/></FinalDraft>");
    Near(6, plain.Layout.TextWidth, "FDX senza impostazioni");
    Check(plain.Layout.Paragraphs.Count == 0, "Nessuna contaminazione fra documenti");
    var malformed = Read("""
        <FinalDraft><PageLayout TopMargin="NaN"><PageSize Width="Infinity" Height="-1"/></PageLayout>
        <ElementSettings Type="Action"><ParagraphSpec LeftIndent="7" RightIndent="2"/></ElementSettings></FinalDraft>
        """);
    Near(8.5, malformed.Layout.PaperWidth, "Fallback misure non valide");
    Check(malformed.Layout.Paragraphs.Count == 0, "Rifiuto intervalli invertiti");
    var a4 = Read("""
        <FinalDraft xmlns="urn:test"><PageLayout><PageSize Width="8.2677" Height="11.6929"/></PageLayout>
        <ElementSettings Type="Action"><ParagraphSpec LeftIndent="1" RightIndent="-1"/></ElementSettings></FinalDraft>
        """);
    Near(6.2677, a4.Layout.TextWidth, "A4, namespace e coordinata destra negativa");
    sp.Elements = new() { new ScreenElement(ElementType.Action, string.Join(" ", Enumerable.Repeat("parola", 40))) };
    var wide = Paginator.Paginate(sp, PageSetup.Letter()).Sum(p => p.Lines.Count);
    sp.Layout.Paragraphs[ElementType.Action].Right = 4;
    var narrow = Paginator.Paginate(sp, PageSetup.Letter()).Sum(p => p.Lines.Count);
    Check(narrow > wide, "Paginazione usa larghezza importata");
    var marked = PageSetup.A4();
    marked.Watermark = "Mario Rossi";
    var pdfBytes = PdfExporter.Build(sp, marked);
    var latin = System.Text.Encoding.Latin1;
    int streamAt = latin.GetString(pdfBytes).IndexOf("stream\n", StringComparison.Ordinal) + 7;
    using var inflate = new System.IO.Compression.ZLibStream(
        new MemoryStream(pdfBytes, streamAt, pdfBytes.Length - streamAt), System.IO.Compression.CompressionMode.Decompress);
    var content = new StreamReader(inflate, latin).ReadToEnd();
    int markAt = content.IndexOf("(MARIO ROSSI)", StringComparison.Ordinal);
    Check(markAt >= 0 && markAt < content.IndexOf("(parola", StringComparison.Ordinal), "Filigrana sotto al testo");
    // revisioni: due modifiche lontane non marcano quello che sta in mezzo, e il colore non conta
    var lines = Enumerable.Range(0, 200).Select(n => new ScreenElement(ElementType.Action, "riga " + n)).ToList();
    var revision = new Revision { Active = true, Snapshot = Revision.TakeSnapshot(lines) };
    lines[3].Text = "cambiata";
    lines[100].Text = "{c:FF0000}riga 100{/c}";
    lines.Insert(150, new ScreenElement(ElementType.Action, "nuova"));
    lines.RemoveAt(180);
    var changedRows = revision.MarkChanged(lines).Select((on, n) => on ? n : -1).Where(n => n >= 0).ToArray();
    Check(changedRows.SequenceEqual(new[] { 3, 150 }), "Revisione: solo le righe cambiate");
    revision.Snapshot = new() { "tutto diverso" };
    Check(revision.MarkChanged(lines).All(on => on), "Revisione: riscrittura completa");

    // FDX: grassetto e corsivo fanno il giro, i caratteri speciali restano caratteri
    var styled = new Screenplay { Elements = new() { new ScreenElement(ElementType.Action, "prima **forte** e *storto* 2 \\* 3 nome\\_file") } };
    FdxIO.Save(fdx, styled);
    Check(FdxIO.Load(fdx).Elements[0].Text == styled.Elements[0].Text, "Round trip FDX con stili");
    Check(StyledText.Plain(StyledText.Escape("a_b *c* {c:FF0000}")) == "a_b *c* {c:FF0000}", "Testo importato letterale");
    Console.WriteLine($"{checks} verifiche superate.");
}
finally { Directory.Delete(folder, true); }
