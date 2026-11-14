using System;
using System.Collections.Generic;
using System.Linq;

namespace DraftLite.Model;

/// <summary>
/// Una bozza congelata. Da quel momento ogni riga cambiata o aggiunta viene marcata
/// con l'asterisco a margine, come si fa sui copioni in lavorazione.
/// I colori seguono l'ordine standard di produzione.
/// </summary>
public sealed class Revision
{
    public static readonly string[] Colors =
    {
        "Bianca", "Blu", "Rosa", "Gialla", "Verde", "Goldenrod", "Salmone", "Ciano",
        "Bianca 2", "Blu 2", "Rosa 2", "Gialla 2"
    };

    /// <summary>Nome del colore della bozza corrente (quella che si sta scrivendo).</summary>
    public string Color { get; set; } = "Bianca";

    /// <summary>Data della bozza, stampata accanto al colore.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Se attiva, le righe cambiate dopo il blocco prendono l'asterisco.</summary>
    public bool Active { get; set; }

    /// <summary>Testo dei paragrafi al momento del blocco: e' il termine di paragone.</summary>
    public List<string> Snapshot { get; set; } = new List<string>();

    public static string NextColor(string current)
    {
        int i = Array.IndexOf(Colors, current ?? "Bianca");
        return i < 0 || i >= Colors.Length - 1 ? Colors[0] : Colors[i + 1];
    }

    public Revision Clone() => new Revision
    {
        Color = Color,
        Date = Date,
        Active = Active,
        Snapshot = new List<string>(Snapshot)
    };

    /// <summary>
    /// Confronta il copione con lo snapshot e dice quali righe sono nuove o modificate.
    /// Prefisso e suffisso uguali restano fuori: cambia solo quello che sta in mezzo.
    /// </summary>
    public bool[] MarkChanged(IList<ScreenElement> elements)
    {
        var flags = new bool[elements.Count];
        if (!Active) return flags;

        var now = elements.Select(e => Key(e.Type + "\u0001" + e.Text)).ToList();
        var before = Snapshot.Select(Key).ToList();

        if (before.Count == 0)
        {
            for (int i = 0; i < flags.Length; i++) flags[i] = true;
            return flags;
        }

        int head = 0;
        while (head < now.Count && head < before.Count && now[head] == before[head]) head++;

        int tail = 0;
        while (tail < now.Count - head && tail < before.Count - head &&
               now[now.Count - 1 - tail] == before[before.Count - 1 - tail]) tail++;

        var added = Added(before.GetRange(head, before.Count - head - tail),
                          now.GetRange(head, now.Count - head - tail));
        for (int i = 0; i < added.Length; i++) flags[head + i] = added[i];
        return flags;
    }

    /// <summary>Il colore non e' una modifica al testo: non deve far scattare l'asterisco.</summary>
    private static string Key(string line) => StyledText.WithoutColors(line);

    /// <summary>
    /// Diff di Myers: dice quali righe di "now" non c'erano in "before", lasciando stare
    /// quelle rimaste uguali anche se in mezzo a due modifiche lontane.
    /// </summary>
    private static bool[] Added(List<string> before, List<string> now)
    {
        int n = before.Count, m = now.Count;
        var added = new bool[m];

        // shortcut: oltre 1500 differenze si marca tutto il tratto, alzare se servono riscritture enormi
        int max = Math.Min(n + m, 1500);
        int o = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();

        for (int d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = (k == -d || (k != d && v[o + k - 1] < v[o + k + 1])) ? v[o + k + 1] : v[o + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && before[x] == now[y]) { x++; y++; }
                v[o + k] = x;
                if (x < n || y < m) continue;

                // trovata la strada: la si ripercorre all'indietro segnando gli inserimenti
                for (int back = d; back > 0; back--)
                {
                    var pv = trace[back];
                    int bk = x - y;
                    int pk = (bk == -back || (bk != back && pv[o + bk - 1] < pv[o + bk + 1])) ? bk + 1 : bk - 1;
                    int px = pv[o + pk], py = px - pk;
                    while (x > px && y > py) { x--; y--; }
                    if (x == px) added[py] = true;
                    x = px; y = py;
                }
                return added;
            }
        }

        for (int i = 0; i < m; i++) added[i] = true;
        return added;
    }

    public static List<string> TakeSnapshot(IEnumerable<ScreenElement> elements)
        => elements.Select(e => e.Type + "\u0001" + e.Text).ToList();
}
