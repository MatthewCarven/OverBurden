using System;
using Godot;

namespace Overburden.Game.Ui;

/// <summary>
/// The Esc menu and the controls card (ALPHA-PLAN B1–B6): Resume, Controls and Quit, and the card
/// it opens, which F1 opens too.
/// </summary>
/// <remarks>
/// <para>
/// <b>It owns no keys.</b> <c>Main</c> routes Esc and F1 here (B1: one place owns Esc) and keeps
/// every other game key off while this is open (B3); the mouse is <c>Main</c>'s to free and take
/// back, through <see cref="Resumed"/>. Quit is <see cref="QuitRequested"/>, so the save path stays
/// the one <c>_ExitTree</c> runs for the window's X.
/// </para>
/// <para>
/// <b>It does not pause</b> (B2): the world runs under it, as it does under the map and a machine's
/// panel. A pause would be the game's first, and the water, the collapse's fuse and the machine
/// tick would all have to agree what it meant.
/// </para>
/// <para>
/// <b>A full-screen shade that stops the mouse</b>, so a click beside the menu is the menu's and
/// never reaches the camera, which would take the mouse back with the menu still up. Above the
/// panel's layer (11), since either can be open over the map.
/// </para>
/// </remarks>
public sealed partial class EscMenu : CanvasLayer
{
    private static readonly Color Shade = new(0f, 0f, 0f, 0.55f);
    private static readonly Color PanelFill = new(0.08f, 0.08f, 0.10f, 0.94f);
    private static readonly Color Border = new(1.00f, 0.86f, 0.45f);
    private static readonly Color DimText = new(0.68f, 0.70f, 0.74f);

    /// <summary>The text size of the menu and the card: a size up from the HUD's default (B12).</summary>
    private const int TextSize = 18;

    private Control _root = null!;
    private Control _menu = null!;
    private Control _card = null!;
    private RichTextLabel _cardText = null!;
    private Button _resume = null!;
    private Button _cardClose = null!;

    /// <summary>Whether the card goes back to the menu when it closes, rather than to the game.</summary>
    private bool _cardFromMenu;

    /// <summary>The menu or the card closed back to the game: the mouse goes back to the camera.</summary>
    public event Action? Resumed;

    /// <summary>Quit was pressed.</summary>
    public event Action? QuitRequested;

    /// <summary>The menu or the card is up.</summary>
    public bool IsOpen => _root.Visible;

    /// <summary>The card is up, from the menu or on its own.</summary>
    public bool CardOpen => _root.Visible && _card.Visible;

    /// <summary>The card's words, set before it is first shown: the developer keys depend on <c>--dev</c>.</summary>
    public string CardText
    {
        get => _cardText.Text;
        set => _cardText.Text = value;
    }

    public override void _Ready()
    {
        Layer = 12;

        _root = new Control { Name = "EscMenuRoot", MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var shade = new ColorRect { Color = Shade, MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(shade);

        _menu = BuildMenu();
        _root.AddChild(_menu);
        _card = BuildCard();
        _root.AddChild(_card);

        _root.Visible = false;
    }

    /// <summary>Opens the menu (Esc with nothing else open).</summary>
    public void OpenMenu()
    {
        _root.Visible = true;
        _card.Visible = false;
        _menu.Visible = true;
        _resume.GrabFocus();
    }

    /// <summary>Opens the card: from the menu it goes back there on close; from F1 or a first open, back to the game.</summary>
    public void OpenCard(bool fromMenu)
    {
        _cardFromMenu = fromMenu;
        _root.Visible = true;
        _menu.Visible = false;
        _card.Visible = true;
        _cardText.ScrollToLine(0);
        _cardClose.Text = fromMenu ? "Back" : "Close";
        _cardClose.GrabFocus();
    }

    /// <summary>
    /// Esc while open: the card goes back to the menu it came from, and anything else goes back to
    /// the game.
    /// </summary>
    public void Back()
    {
        if (CardOpen && _cardFromMenu)
        {
            OpenMenu();
            return;
        }

        Close();
    }

    /// <summary>Closes everything and hands the mouse back (<see cref="Resumed"/>).</summary>
    public void Close()
    {
        if (!_root.Visible)
        {
            return;
        }

        _root.Visible = false;
        GetViewport()?.GuiReleaseFocus();
        Resumed?.Invoke();
    }

    /// <summary>
    /// Says it is saving, then quits a moment later (B4): the save in <c>_ExitTree</c> can wait up
    /// to 5 s for a water tick on a worker, and a window that freezes with "Quit" still on it reads
    /// as a hang. The wait is two drawn frames' worth, so "Saving…" is on screen first.
    /// </summary>
    private void Quit(Button button)
    {
        button.Text = "Saving…";
        button.Disabled = true;
        _resume.Disabled = true;
        GetTree().CreateTimer(0.1).Timeout += () => QuitRequested?.Invoke();
    }

    private Control BuildMenu()
    {
        PanelContainer frame = Frame("EscMenuFrame", 340f);
        frame.AnchorTop = 0.5f;
        frame.AnchorBottom = 0.5f;
        frame.GrowVertical = Control.GrowDirection.Both;

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 10);
        frame.AddChild(column);

        var title = new Label
        {
            Text = "Overburden",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeColorOverride("font_color", Border);
        title.AddThemeFontSizeOverride("font_size", 26);
        column.AddChild(title);

        var subtitle = new Label
        {
            Text = "The world keeps going while this is open.",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        subtitle.AddThemeColorOverride("font_color", DimText);
        subtitle.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(subtitle);

        _resume = MenuButton("Resume", Close);
        column.AddChild(_resume);
        column.AddChild(MenuButton("Controls", () => OpenCard(fromMenu: true)));
        Button quit = null!;
        quit = MenuButton("Quit", () => Quit(quit));
        column.AddChild(quit);

        var hint = new Label
        {
            Text = "Quitting saves the world.",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hint.AddThemeColorOverride("font_color", DimText);
        hint.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(hint);

        return frame;
    }

    private Control BuildCard()
    {
        PanelContainer frame = Frame("ControlsCardFrame", 780f);

        // Tall with the window rather than a fixed height: the card scrolls, and the more of it
        // shows at once the less it has to.
        frame.AnchorTop = 0.06f;
        frame.AnchorBottom = 0.94f;

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 8);
        frame.AddChild(column);

        var title = new Label { Text = "Controls", MouseFilter = Control.MouseFilterEnum.Ignore };
        title.AddThemeColorOverride("font_color", Border);
        title.AddThemeFontSizeOverride("font_size", 26);
        column.AddChild(title);

        _cardText = new RichTextLabel
        {
            Name = "ControlsText",
            BbcodeEnabled = true,
            ScrollActive = true,
            SelectionEnabled = false,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _cardText.AddThemeFontSizeOverride("normal_font_size", TextSize);
        _cardText.AddThemeFontSizeOverride("bold_font_size", TextSize);
        _cardText.AddThemeConstantOverride("table_h_separation", 18);
        _cardText.AddThemeConstantOverride("table_v_separation", 4);
        column.AddChild(_cardText);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        var hint = new Label
        {
            Text = "F1 or Esc to close   ·   the mouse wheel scrolls",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        hint.AddThemeColorOverride("font_color", DimText);
        hint.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(hint);

        _cardClose = MenuButton("Close", Back);
        _cardClose.CustomMinimumSize = new Vector2(140f, 40f);
        row.AddChild(_cardClose);
        column.AddChild(row);

        return frame;
    }

    /// <summary>
    /// A centred frame in the machine panel's style, <paramref name="width"/> wide: anchors and
    /// offsets set directly, never a preset plus a size (the rule <c>Hud.Bar</c> records).
    /// </summary>
    private static PanelContainer Frame(string name, float width)
    {
        var style = new StyleBoxFlat
        {
            BgColor = PanelFill,
            BorderColor = Border,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 22,
            ContentMarginRight = 22,
            ContentMarginTop = 16,
            ContentMarginBottom = 16,
        };
        style.SetBorderWidthAll(2);

        var frame = new PanelContainer { Name = name, MouseFilter = Control.MouseFilterEnum.Stop };
        frame.AddThemeStyleboxOverride("panel", style);
        frame.AnchorLeft = 0.5f;
        frame.AnchorRight = 0.5f;
        frame.OffsetLeft = -width * 0.5f;
        frame.OffsetRight = width * 0.5f;
        frame.GrowHorizontal = Control.GrowDirection.Both;
        return frame;
    }

    private static Button MenuButton(string text, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0f, 44f),
            FocusMode = Control.FocusModeEnum.All,
        };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += pressed;
        return button;
    }
}
