using System.Text;

namespace Overburden.Game.Ui;

/// <summary>
/// The controls card's words (ALPHA-PLAN B5): every key in plain words, the developer keys under
/// their own heading and only under <c>--dev</c>, and the credits last. Shown by
/// <see cref="EscMenu"/> from the menu, on F1, and once at a new world's first open in an export.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written from <c>QUICKSTART.md</c>'s "All the keys", and a second copy of it.</b> The two are
/// kept in step by hand: change a key here and change it there. When keys become rebindable this
/// is generated from the binding table instead, and the copy goes (the decision's revisit).
/// </para>
/// <para>
/// BBCode for Godot's <c>RichTextLabel</c>: a two-column table a section, the key bright and its
/// meaning plain. No square brackets in the words, which BBCode would read as tags.
/// </para>
/// </remarks>
public static class ControlsCard
{
    private const string KeyColour = "#ffdb73";
    private const string HeadColour = "#e8e8ee";
    private const string NoteColour = "#adb3bd";

    /// <summary>The card's text, with the developer keys when <paramref name="dev"/> is set.</summary>
    public static string Text(bool dev, string version)
    {
        var text = new StringBuilder();

        Section(text, "Moving");
        text.Append("[table=3]");
        Row3(text, "", "On foot", "Flying", header: true);
        Row3(text, "W A S D", "walk", "fly where you look");
        Row3(text, "Space", "jump", "up");
        Row3(text, "Shift", "sneak (you won't walk off an edge)", "down");
        Row3(text, "Ctrl", "sprint", "fast");
        Row3(text, "Alt", "", "slow");
        Row3(text, "F", "switch to flying", "switch to walking");
        Row3(text, "Mouse", "look", "look");
        text.Append("[/table]\n");
        Note(text, "Flying passes through rock.");

        Section(text, "Digging and tools");
        text.Append("[table=2]");
        Row(text, "Left mouse", "dig with the tool in hand");
        Row(text, "Alt + left mouse", "dig a single small block");
        Row(text, "Hold Q", "keep digging with the tool in hand, no clicking");
        Row(text, "T", "next tool: hand pick, quantum miner (1 m cube), quantum tunneller (2 m cube), quantum excavator (a whole 8 m chunk)");
        text.Append("[/table]\n");
        Note(text, "The hand pick is free and takes soft ground (dirt, grass, clay), not stone. The quantum tools take anything, at range, paid for from the pack you wear.");

        Section(text, "Building and the hotbar");
        text.Append("[table=2]");
        Row(text, "Right mouse", "place the selected material in the brush's shape (at range with a quantum tool)");
        Row(text, "Alt + right mouse", "place a single small block");
        Row(text, "B", "next brush: cube, slab, ramp, sphere");
        Row(text, "1 – 9", "select a hotbar slot");
        Row(text, "V", "step through everything you carry, past slot 9 too");
        Row(text, "Ctrl+Z", "undo (the last three steps; a collapse clears it)");
        text.Append("[/table]\n");

        Section(text, "Power and packs");
        text.Append("[table=2]");
        Row(text, "Right mouse, pack in hand", "wear it (the one you wore goes back in your pocket, with its charge)");
        Row(text, "P", "a kit: 100 of every machine, material and pack (a test key)");
        Row(text, "Alt+P", "fill every placed power cell, and the pack you wear (a test key)");
        text.Append("[/table]\n");
        Note(text, "A worn pack recharges slowly from charged vacuum cells on its channel. Solar panels charge cells under open sky.");

        Section(text, "Machines");
        text.Append("[table=2]");
        Row(text, "Right mouse, computer in hand, on a machine", "open its panel: channel, on/off, priority");
        Row(text, "Right mouse, computer in hand, on nothing", "open your pack's panel");
        text.Append("[/table]\n");
        Note(text, "A relay pump takes water where it stands, and a relay outlet on the same channel lets it out elsewhere. Dig a machine with a quantum tool to pick it up.");

        Section(text, "Water (test keys)");
        text.Append("[table=2]");
        Row(text, "R", "pour 10,000 litres where you're looking");
        Row(text, "Alt+R or Alt+G", "take back the water within 2 m of where you're looking");
        Row(text, "L", "place a free lamp");
        text.Append("[/table]\n");

        Section(text, "The view and the game");
        text.Append("[table=2]");
        Row(text, "Esc", "this menu, or close a panel or the map");
        Row(text, "F1", "these controls");
        Row(text, "F10", "see further: a step each press, then back to normal");
        Row(text, "F11 or Alt+Enter", "fullscreen");
        Row(text, "F12", "the map");
        text.Append("[/table]\n");
        Note(text, "The world saves when you quit, and every 30 seconds.");

        if (dev)
        {
            Section(text, "Developer keys (--dev)");
            text.Append("[table=2]");
            Row(text, "Ctrl+R", "pour 1,000,000 litres (no undo)");
            Row(text, "H", "hold the water, and pumps, rain and roofs with it; H again releases");
            Row(text, "Alt+H", "a full memory clean-up, its time in the log");
            text.Append("[/table]\n");
        }

        text.Append($"\n[color={NoteColour}]{version}, by Matthew Carven.\n");
        text.Append("Music: Korobeiniki (Russian folk song, public domain), arranged by Matthew Carven.\n");
        text.Append("Made with the Godot Engine and .NET. Their licences are in THIRD-PARTY-NOTICES.md beside the game.[/color]");
        return text.ToString();
    }

    private static void Section(StringBuilder text, string title) =>
        text.Append($"\n[font_size=21][color={HeadColour}]{title}[/color][/font_size]\n");

    private static void Note(StringBuilder text, string note) =>
        text.Append($"[color={NoteColour}]{note}[/color]\n");

    private static void Row(StringBuilder text, string key, string meaning) =>
        text.Append($"[cell expand=2][color={KeyColour}]{key}[/color][/cell][cell expand=5]{meaning}[/cell]");

    private static void Row3(StringBuilder text, string key, string foot, string flying, bool header = false)
    {
        string Wrap(string s) => header ? $"[color={NoteColour}]{s}[/color]" : s;
        text.Append(
            $"[cell expand=2][color={KeyColour}]{key}[/color][/cell][cell expand=3]{Wrap(foot)}[/cell][cell expand=3]{Wrap(flying)}[/cell]");
    }
}
