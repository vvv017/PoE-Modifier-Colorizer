using System.Diagnostics;
using System.Drawing;
using GgpkColorizer.Archive;

namespace GgpkColorizer;

public sealed class MainForm : Form
{
    private static readonly Color PreviewBackground = Color.FromArgb(32, 32, 32);
    private static readonly Color PreviewSelectionBackground = Color.FromArgb(64, 64, 64);
    private static readonly Color PreviewDefaultText = Color.Gainsboro;

    private sealed record Choice(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppController _controller;
    private readonly TextBox _archivePath = new() { Dock = DockStyle.Fill };
    private readonly Button _browse = new() { AutoSize = true };
    private readonly Button _update = new() { AutoSize = true };
    private readonly Button _compare = new() { AutoSize = true };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _modifiersTab = new();
    private readonly TabPage _comparisonTab = new();
    private readonly TabPage _historyTab = new();
    private readonly TextBox _search = new() { Width = 260 };
    private readonly Label _gameLanguageLabel = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 0) };
    private readonly ComboBox _gameLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox _group = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 165 };
    private readonly FlowLayoutPanel _altarFilters = new() { AutoSize = true, WrapContents = false, Visible = false };
    private readonly Label _factionLabel = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 0) };
    private readonly ComboBox _faction = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly Label _targetLabel = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 0) };
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly Label _sideLabel = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 0) };
    private readonly ComboBox _side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly ComboBox _color = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly Button _setColor = new() { AutoSize = true };
    private readonly Button _clearColor = new() { AutoSize = true };
    private readonly Button _applySelected = new() { AutoSize = true };
    private readonly Button _applyRules = new() { AutoSize = true };
    private readonly GroupBox _catalogNoticesGroup = new() { Dock = DockStyle.Fill, Height = 84, Visible = false };
    private readonly TextBox _catalogNotices = CreateNoticeBox();
    private readonly DataGridView _modifierGrid = CreateGrid();
    private readonly GroupBox _previewGroup = new() { Dock = DockStyle.Fill };
    private readonly TextBox _preview = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = PreviewBackground,
        ForeColor = PreviewDefaultText,
    };
    private readonly GroupBox _comparisonNoticesGroup = new() { Dock = DockStyle.Fill, Height = 84, Visible = false };
    private readonly TextBox _comparisonNotices = CreateNoticeBox();
    private readonly DataGridView _comparisonGrid = CreateGrid();
    private readonly Button _undoSelected = new() { AutoSize = true };
    private readonly Button _undoBatch = new() { AutoSize = true };
    private readonly DataGridView _historyGrid = CreateGrid();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<Control> _mutationControls = [];
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _comparisonCancellation;
    private UiText _text;
    private bool _busy;
    private bool _writing;
    private bool _closePending;
    private bool _changingLanguage;
    private bool _changingGameLanguage;
    private bool _changingAltarFilters;

    public MainForm(AppController controller)
    {
        _controller = controller;
        _text = UiText.For(controller.Settings.Language);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(920, 620);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        ConfigureGrids();
        WireEvents();
        LoadState();
        ApplyLanguage();
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.Fixed3D,
        MultiSelect = true,
        ReadOnly = true,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    };

    private static TextBox CreateNoticeBox() => new()
    {
        BackColor = SystemColors.Window,
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
    };

    private void BuildLayout()
    {
        var header = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 6,
            Dock = DockStyle.Top,
            Padding = new Padding(8, 8, 8, 4),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++)
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Name = "archiveLabel", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        header.Controls.Add(_archivePath, 1, 0);
        header.Controls.Add(_browse, 2, 0);
        header.Controls.Add(_update, 3, 0);
        header.Controls.Add(_compare, 4, 0);
        var languagePanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        languagePanel.Controls.Add(new Label { Name = "languageLabel", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
        languagePanel.Controls.Add(_language);
        header.Controls.Add(languagePanel, 5, 0);

        BuildModifierTab();
        BuildComparisonTab();
        BuildHistoryTab();
        _tabs.TabPages.AddRange([_modifiersTab, _comparisonTab, _historyTab]);
        _statusStrip.Items.Add(_status);
        Controls.Add(_tabs);
        Controls.Add(header);
        Controls.Add(_statusStrip);

        _mutationControls.AddRange([_archivePath, _browse, _update, _setColor, _clearColor, _applySelected, _applyRules, _undoSelected, _undoBatch]);
    }

    private void BuildModifierTab()
    {
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _altarFilters.Controls.AddRange([
            _factionLabel,
            _faction,
            _targetLabel,
            _target,
            _sideLabel,
            _side,
        ]);
        filters.Controls.AddRange([
            _search,
            _gameLanguageLabel,
            _gameLanguage,
            _group,
            _altarFilters,
            _color,
            _setColor,
            _clearColor,
        ]);
        _catalogNoticesGroup.Controls.Add(_catalogNotices);
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Size = new Size(800, 520),
            SplitterDistance = 390,
            Panel2MinSize = 100,
        };
        split.Panel1.Controls.Add(_modifierGrid);
        _previewGroup.Controls.Add(_preview);
        split.Panel2.Controls.Add(_previewGroup);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        actions.Controls.AddRange([_applyRules, _applySelected]);
        layout.Controls.Add(filters, 0, 0);
        layout.Controls.Add(_catalogNoticesGroup, 0, 1);
        layout.Controls.Add(split, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        _modifiersTab.Controls.Add(layout);
    }

    private void BuildComparisonTab()
    {
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _comparisonNoticesGroup.Controls.Add(_comparisonNotices);
        layout.Controls.Add(_comparisonNoticesGroup, 0, 0);
        layout.Controls.Add(_comparisonGrid, 0, 1);
        _comparisonTab.Controls.Add(layout);
    }

    private void BuildHistoryTab()
    {
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        actions.Controls.AddRange([_undoBatch, _undoSelected]);
        layout.Controls.Add(_historyGrid, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        _historyTab.Controls.Add(layout);
    }

    private void ConfigureGrids()
    {
        _modifierGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "group", Width = 115 });
        _modifierGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "text", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _modifierGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "altar", Width = 315 });
        _modifierGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "color", Width = 110 });
        _modifierGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "comparison", Width = 110 });

        _comparisonGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", Width = 180 });
        _comparisonGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "local", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 });
        _comparisonGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "website", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 });
        _comparisonGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "detail", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 20 });
        _comparisonGrid.Columns.Add(new DataGridViewLinkColumn { Name = "source", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 10, TrackVisitedState = true });

        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "time", Width = 145 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "action", Width = 75 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", Width = 105 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "language", Width = 105 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "text", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "before", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 25 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "after", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 25 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "detail", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 15 });
    }

    private void WireEvents()
    {
        _browse.Click += (_, _) => ChooseArchive();
        _update.Click += async (_, _) => await ImportAsync();
        _compare.Click += async (_, _) => await CompareAsync();
        _language.SelectedIndexChanged += (_, _) => ChangeLanguage();
        _search.TextChanged += (_, _) => PopulateModifiers();
        _gameLanguage.SelectedIndexChanged += (_, _) => ChangeGameLanguage();
        _group.SelectedIndexChanged += (_, _) => ChangeGroupFilter();
        _faction.SelectedIndexChanged += (_, _) => ChangeAltarFilter();
        _target.SelectedIndexChanged += (_, _) => ChangeAltarFilter();
        _side.SelectedIndexChanged += (_, _) => ChangeAltarFilter();
        _setColor.Click += (_, _) => SetSelectedColor(clear: false);
        _clearColor.Click += (_, _) => SetSelectedColor(clear: true);
        _modifierGrid.SelectionChanged += (_, _) => UpdatePreview();
        _comparisonGrid.CellContentClick += OpenComparisonSource;
        _applySelected.Click += async (_, _) => await ApplyAsync(SelectedModifierKeys());
        _applyRules.Click += async (_, _) => await ApplyAsync(ConfiguredModifierKeys());
        _undoSelected.Click += async (_, _) => await UndoAsync(SelectedChangeIds());
        _undoBatch.Click += async (_, _) => await UndoAsync(LatestBatchChangeIds());
        FormClosing += HandleFormClosing;
    }

    private void LoadState()
    {
        _changingLanguage = true;
        _language.Items.AddRange([
            new Choice("zh-TW", "繁體中文"),
            new Choice("en", "English"),
        ]);
        _language.SelectedIndex = _controller.Settings.Language.Equals("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _changingLanguage = false;
        _archivePath.Text = _controller.Settings.ArchivePath;
        PopulateGameLanguages();
        PopulateGroups();
        PopulateAltarFilters();
        PopulateColors();
        PopulateCatalogNotices();
        PopulateModifiers();
        PopulateComparison();
        PopulateHistory();
    }

    private void ApplyLanguage()
    {
        Text = _text["title"];
        FindControl("archiveLabel").Text = _text["archive"];
        FindControl("languageLabel").Text = _text["language"];
        _browse.Text = _text["browse"];
        _update.Text = _text["update"];
        _compare.Text = _text["compare"];
        _modifiersTab.Text = _text["tab.modifiers"];
        _comparisonTab.Text = _text["tab.comparison"];
        _historyTab.Text = _text["tab.history"];
        _search.PlaceholderText = _text["search"];
        _gameLanguageLabel.Text = _text["gameLanguage"];
        _factionLabel.Text = _text["filter.faction"];
        _targetLabel.Text = _text["filter.target"];
        _sideLabel.Text = _text["filter.side"];
        _setColor.Text = _text["setColor"];
        _clearColor.Text = _text["clearColor"];
        _applySelected.Text = _text["applySelected"];
        _applyRules.Text = _text["applyRules"];
        _catalogNoticesGroup.Text = _text["notices.catalog"];
        _previewGroup.Text = _text["preview"];
        _comparisonNoticesGroup.Text = _text["notices.comparison"];
        _undoSelected.Text = _text["undoSelected"];
        _undoBatch.Text = _text["undoBatch"];
        SetColumnText(_modifierGrid, "group", "column.group");
        SetColumnText(_modifierGrid, "text", "column.text");
        SetColumnText(_modifierGrid, "altar", "column.altar");
        SetColumnText(_modifierGrid, "color", "column.color");
        SetColumnText(_modifierGrid, "comparison", "column.comparison");
        SetColumnText(_comparisonGrid, "status", "column.status");
        SetColumnText(_comparisonGrid, "local", "column.local");
        SetColumnText(_comparisonGrid, "website", "column.website");
        SetColumnText(_comparisonGrid, "detail", "column.detail");
        SetColumnText(_comparisonGrid, "source", "column.source");
        SetColumnText(_historyGrid, "time", "column.time");
        SetColumnText(_historyGrid, "action", "column.action");
        SetColumnText(_historyGrid, "status", "column.status");
        SetColumnText(_historyGrid, "language", "column.language");
        SetColumnText(_historyGrid, "text", "column.text");
        SetColumnText(_historyGrid, "before", "column.before");
        SetColumnText(_historyGrid, "after", "column.after");
        SetColumnText(_historyGrid, "detail", "column.detail");
        ApplyAccessibilityText();
        PopulateGameLanguages();
        PopulateGroups();
        PopulateAltarFilters();
        PopulateCatalogNotices();
        PopulateModifiers();
        PopulateComparison();
        PopulateHistory();
        _compare.Enabled = _controller.Catalog is not null && !_busy;
        if (!_busy)
            SetStatus(_controller.Catalog is null ? _text["empty"] : _text["ready"]);
    }

    private void ApplyAccessibilityText()
    {
        _archivePath.AccessibleName = _text["archive"];
        _browse.AccessibleName = _text["browse"];
        _update.AccessibleName = _text["update"];
        _compare.AccessibleName = _text["compare"];
        _search.AccessibleName = _text["search"];
        _gameLanguage.AccessibleName = _text["gameLanguage"];
        _group.AccessibleName = _text["column.group"];
        _faction.AccessibleName = _text["filter.faction"];
        _target.AccessibleName = _text["filter.target"];
        _side.AccessibleName = _text["filter.side"];
        _color.AccessibleName = _text["color"];
        _modifierGrid.AccessibleName = _text["tab.modifiers"];
        _catalogNotices.AccessibleName = _text["notices.catalog"];
        _comparisonGrid.AccessibleName = _text["tab.comparison"];
        _comparisonNotices.AccessibleName = _text["notices.comparison"];
        _historyGrid.AccessibleName = _text["tab.history"];
    }

    private Control FindControl(string name) => GetAllControls(this).First(control => control.Name == name);

    private static IEnumerable<Control> GetAllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in GetAllControls(child))
                yield return descendant;
        }
    }

    private void SetColumnText(DataGridView grid, string name, string textKey) => grid.Columns[name].HeaderText = _text[textKey];

    private void PopulateGameLanguages()
    {
        string selected = (_gameLanguage.SelectedItem as Choice)?.Code ??
            _controller.Settings.GameLanguage;
        _changingGameLanguage = true;
        try
        {
            _gameLanguage.BeginUpdate();
            _gameLanguage.Items.Clear();
            _gameLanguage.Items.AddRange([
                new Choice("Traditional Chinese", _text["gameLanguage.traditionalChinese"]),
                new Choice("English", _text["gameLanguage.english"]),
            ]);
            _gameLanguage.SelectedItem = _gameLanguage.Items.Cast<Choice>()
                .FirstOrDefault(choice => choice.Code == selected) ?? _gameLanguage.Items[0];
            _gameLanguage.EndUpdate();
        }
        finally
        {
            _changingGameLanguage = false;
        }
    }

    private void PopulateGroups()
    {
        string selected = (_group.SelectedItem as Choice)?.Code ?? "all";
        _group.BeginUpdate();
        _group.Items.Clear();
        foreach (string code in new[] { "all", "map", "altar" })
            _group.Items.Add(new Choice(code, _text["group." + code]));
        _group.SelectedItem = _group.Items.Cast<Choice>().First(choice => choice.Code == selected);
        _group.EndUpdate();
    }

    private void PopulateAltarFilters()
    {
        _changingAltarFilters = true;
        try
        {
            PopulateChoices(_faction, "altar.faction", ["all", "eater", "exarch"]);
            PopulateChoices(_target, "altar.target", ["all", "player", "minions", "boss"]);
            PopulateChoices(_side, "altar.side", ["all", "upside", "downside"]);
        }
        finally
        {
            _changingAltarFilters = false;
        }
        UpdateAltarFilterVisibility();
    }

    private void PopulateChoices(ComboBox comboBox, string textPrefix, string[] codes)
    {
        string selected = SelectedCode(comboBox);
        comboBox.BeginUpdate();
        comboBox.Items.Clear();
        foreach (string code in codes)
            comboBox.Items.Add(new Choice(code, _text[textPrefix + "." + code]));
        comboBox.SelectedItem = comboBox.Items.Cast<Choice>()
            .FirstOrDefault(choice => choice.Code == selected) ?? comboBox.Items[0];
        comboBox.EndUpdate();
    }

    private void ChangeGroupFilter()
    {
        UpdateAltarFilterVisibility();
        PopulateModifiers();
    }

    private void ChangeAltarFilter()
    {
        if (!_changingAltarFilters)
            PopulateModifiers();
    }

    private void UpdateAltarFilterVisibility() =>
        _altarFilters.Visible = SelectedCode(_group) == "altar";

    private static string SelectedCode(ComboBox comboBox) =>
        (comboBox.SelectedItem as Choice)?.Code ?? "all";

    private void PopulateColors()
    {
        string? selected = (_color.SelectedItem as GameColor)?.Name;
        _color.BeginUpdate();
        _color.Items.Clear();
        if (_controller.Catalog is not null)
            _color.Items.AddRange(_controller.Catalog.Colors.Cast<object>().ToArray());
        _color.DisplayMember = nameof(GameColor.Name);
        if (selected is not null)
            _color.SelectedItem = _color.Items.Cast<GameColor>().FirstOrDefault(color => color.Name == selected);
        if (_color.SelectedIndex < 0 && _color.Items.Count > 0)
            _color.SelectedIndex = 0;
        _color.EndUpdate();
    }

    private void PopulateModifiers()
    {
        string query = _search.Text.Trim();
        string gameLanguage = (_gameLanguage.SelectedItem as Choice)?.Code ??
            "Traditional Chinese";
        string group = SelectedCode(_group);
        string faction = SelectedCode(_faction);
        string target = SelectedCode(_target);
        string side = SelectedCode(_side);
        var comparisonByKey = CurrentComparison()?.Items
            .GroupBy(item => item.ModifierKey)
            .ToDictionary(items => items.Key, items => ComparisonSummary(items));
        _modifierGrid.SuspendLayout();
        _modifierGrid.Rows.Clear();
        if (_controller.Catalog is not null)
        {
            foreach (Modifier modifier in _controller.Catalog.Items)
            {
                if (!modifier.Language.Equals(gameLanguage, StringComparison.Ordinal) ||
                    !MatchesGroup(modifier, group) ||
                    !MatchesAltarFilters(modifier, group, faction, target, side))
                    continue;
                string display = Display(modifier.Template);
                string altarContext = DisplayAltarUses(modifier);
                if (query.Length > 0 && !display.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !modifier.StatIds.Any(id => id.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                    !altarContext.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                _controller.Settings.Rules.TryGetValue(modifier.Key, out string? colorName);
                ComparisonItem? comparison = null;
                comparisonByKey?.TryGetValue(modifier.Key, out comparison);
                int index = _modifierGrid.Rows.Add(
                    DisplayGroup(modifier),
                    display,
                    altarContext,
                    colorName ?? "",
                    comparison is null ? _text.Status("unchecked") : _text.Status(comparison.Status));
                DataGridViewRow row = _modifierGrid.Rows[index];
                row.Tag = modifier;
                DataGridViewCell textCell = row.Cells["text"];
                textCell.Style.BackColor = PreviewBackground;
                textCell.Style.SelectionBackColor = PreviewSelectionBackground;
                Color textColor = PreviewDefaultText;
                if (TryGetColor(colorName, out Color color))
                    textColor = color;
                textCell.Style.ForeColor = textColor;
                textCell.Style.SelectionForeColor = textColor;
                if (comparison?.Status == "template-matched")
                    row.Cells["comparison"].ToolTipText = _text["comparison.templateMatchedNotice"];
            }
        }
        _modifierGrid.ResumeLayout();
        UpdatePreview();
    }

    private void PopulateCatalogNotices()
    {
        ModifierCatalog? catalog = _controller.Catalog;
        IEnumerable<string> notices = catalog?.Notices ?? [];
        if (catalog is not null)
        {
            var catalogKeys = catalog.Items
                .Select(item => item.Key)
                .ToHashSet(StringComparer.Ordinal);
            int orphanRuleCount = _controller.Settings.Rules.Keys
                .Count(key => !catalogKeys.Contains(key));
            if (orphanRuleCount > 0)
            {
                notices = new[]
                {
                    string.Format(_text["notices.orphanRules"], orphanRuleCount),
                }.Concat(notices);
            }
        }
        SetNotices(_catalogNoticesGroup, _catalogNotices, notices);
    }

    private static bool MatchesGroup(Modifier modifier, string group) => group switch
    {
        "all" => true,
        "altar" => modifier.Group.Equals("altar", StringComparison.OrdinalIgnoreCase) ||
            modifier.AltarUses.Count > 0,
        _ => modifier.Group.Equals(group, StringComparison.OrdinalIgnoreCase),
    };

    private static bool MatchesAltarFilters(
        Modifier modifier,
        string group,
        string faction,
        string target,
        string side)
    {
        if (group != "altar" ||
            (faction == "all" && target == "all" && side == "all"))
        {
            return true;
        }

        return modifier.AltarUses.Any(use =>
            (faction == "all" || use.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase)) &&
            (target == "all" || use.Target.Equals(target, StringComparison.OrdinalIgnoreCase)) &&
            (side == "all" || use.Side.Equals(side, StringComparison.OrdinalIgnoreCase)));
    }

    private string DisplayGroup(Modifier modifier) =>
        modifier.Group.Equals("map", StringComparison.OrdinalIgnoreCase) &&
        modifier.AltarUses.Count > 0
            ? _text["group.mapAltar"]
            : _text["group." + modifier.Group.ToLowerInvariant()];

    private string DisplayAltarUses(Modifier modifier) => string.Join(
        "; ",
        modifier.AltarUses
            .Select(DisplayAltarUse)
            .Distinct(StringComparer.Ordinal));

    private string DisplayAltarUse(AltarUse use) => string.Join(
        " · ",
        _text["altar.faction." + use.Faction],
        _text["altar.target." + use.Target],
        _text["altar.side." + use.Side]);

    private void PopulateComparison()
    {
        _comparisonGrid.SuspendLayout();
        _comparisonGrid.Rows.Clear();
        ComparisonReport? report = CurrentComparison();
        IEnumerable<string> notices = report?.Notices ?? [];
        if (report?.Items.Any(item => item.Status == "template-matched") == true)
        {
            notices = new[] { _text["comparison.templateMatchedNotice"] }
                .Concat(notices);
        }
        SetNotices(_comparisonNoticesGroup, _comparisonNotices, notices);
        if (report is not null)
        {
            foreach (ComparisonItem item in report.Items.Where(item => item.Status != "matched"))
            {
                int index = _comparisonGrid.Rows.Add(
                    _text.Status(item.Status),
                    Display(item.LocalText),
                    Display(item.WebsiteText),
                    item.Detail,
                    item.SourceUrl);
                _comparisonGrid.Rows[index].Tag = item;
            }
        }
        _comparisonGrid.ResumeLayout();
    }

    private void PopulateHistory()
    {
        _historyGrid.SuspendLayout();
        _historyGrid.Rows.Clear();
        foreach (ChangeRecord change in _controller.History.OrderByDescending(item => item.CreatedAt))
        {
            int index = _historyGrid.Rows.Add(
                change.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                _text[change.UndoOf is null ? "action.change" : "action.undo"],
                _text.Status(change.Status),
                DisplayHistoryLanguage(change),
                Display(change.Modifier.Template),
                change.RuleChanges.Count > 1
                    ? string.Join(" / ", change.RuleChanges.Select(rule => Display(rule.Before)))
                    : Display(change.Before),
                change.RuleChanges.Count > 1
                    ? string.Join(" / ", change.RuleChanges.Select(rule => Display(rule.After)))
                    : Display(change.After),
                change.Detail);
            _historyGrid.Rows[index].Tag = change;
        }
        _historyGrid.ResumeLayout();
    }

    private ComparisonReport? CurrentComparison()
    {
        ComparisonReport? report = _controller.Comparison;
        return report is not null && report.SnapshotId == _controller.Catalog?.SnapshotId ? report : null;
    }

    private void UpdatePreview()
    {
        Modifier? modifier = _modifierGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Tag as Modifier)
            .FirstOrDefault(item => item is not null);
        if (modifier is null)
        {
            _preview.Text = "";
        }
        else
        {
            ComparisonItem? comparison = ComparisonSummary(CurrentComparison()?.Items
                .Where(item => item.ModifierKey == modifier.Key));
            List<string> previewLines =
            [
                Display(modifier.Template),
                "",
                $"{_text["preview.language"]}: {DisplayGameLanguage(modifier.Language)}",
                $"{_text["preview.source"]}: {modifier.ResourcePath}",
                $"{_text["preview.stats"]}: {string.Join(", ", modifier.StatIds)}",
                $"{_text["preview.variant"]}: {modifier.VariantKey}",
                $"{_text["preview.comparison"]}: {_text.Status(comparison?.Status ?? "unchecked")}",
            ];
            if (AltarResistanceScope.IsScoped(modifier))
                previewLines.Add(_text["preview.altarResistanceScope"]);
            if (modifier.AltarUses.Count > 0)
            {
                previewLines.Add("");
                previewLines.Add(_text["preview.altarUses"] + ":");
                previewLines.AddRange(modifier.AltarUses
                    .Select(use => $"• {DisplayAltarUse(use)} — {_text["altar.modId"]}: {use.ModId}")
                    .Distinct(StringComparer.Ordinal));
            }
            _preview.Lines = previewLines.ToArray();
        }
        _preview.ForeColor = modifier is not null &&
            _controller.Settings.Rules.TryGetValue(modifier.Key, out string? colorName) &&
            TryGetColor(colorName, out Color color)
            ? color
            : PreviewDefaultText;
    }

    private static ComparisonItem? ComparisonSummary(IEnumerable<ComparisonItem>? items) =>
        items?.OrderBy(item => item.Status switch
        {
            "matched" => 4,
            "template-matched" => 3,
            "ambiguous" => 2,
            "unmatched" or "different" => 1,
            _ => 0,
        }).FirstOrDefault();

    private string DisplayHistoryLanguage(ChangeRecord change) =>
        change.RuleChanges.Count > 1
            ? _text["gameLanguage.both"]
            : DisplayGameLanguage(change.Modifier.Language);

    private string DisplayGameLanguage(string language) => language switch
    {
        "Traditional Chinese" => _text["gameLanguage.traditionalChinese"],
        "English" => _text["gameLanguage.english"],
        _ => language,
    };

    private static void SetNotices(GroupBox group, TextBox textBox, IEnumerable<string> notices)
    {
        string[] visibleNotices = notices
            .Where(notice => !string.IsNullOrWhiteSpace(notice))
            .Distinct(StringComparer.Ordinal)
            .Select(notice => "• " + notice)
            .ToArray();
        textBox.Lines = visibleNotices;
        group.Visible = visibleNotices.Length > 0;
    }

    private static string Display(string raw) => GgpkColorizer.Text.TextMarkup.Display(raw);

    private bool TryGetColor(string? name, out Color result)
    {
        GameColor? gameColor = _controller.Catalog?.Colors.FirstOrDefault(color => color.Name == name);
        try
        {
            result = gameColor is null ? default : ColorTranslator.FromHtml(gameColor.Hex);
            return gameColor is not null;
        }
        catch (Exception) when (gameColor is not null)
        {
            result = default;
            return false;
        }
    }

    private void SetSelectedColor(bool clear)
    {
        Modifier[] selected = SelectedModifiers();
        if (selected.Length == 0)
        {
            SetStatus(_text["noSelection"]);
            return;
        }
        string? colorName = clear ? null : (_color.SelectedItem as GameColor)?.Name;
        if (!clear && colorName is null)
        {
            SetStatus(_text["noColor"]);
            return;
        }
        foreach (Modifier modifier in selected)
            _controller.SetRule(modifier.Key, colorName);
        TrySaveSettings();
        PopulateModifiers();
    }

    private void ChooseArchive()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Path of Exile Content.ggpk|Content.ggpk|GGPK files (*.ggpk)|*.ggpk|All files (*.*)|*.*",
            FileName = "Content.ggpk",
            Title = _text["chooseArchive"],
        };
        if (File.Exists(_archivePath.Text))
            dialog.InitialDirectory = Path.GetDirectoryName(_archivePath.Text);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _archivePath.Text = dialog.FileName;
        _controller.Settings.ArchivePath = dialog.FileName;
        TrySaveSettings();
    }

    private async Task ImportAsync()
    {
        if (!EnsureArchivePath())
            return;
        string? snapshot = null;
        await RunExclusiveAsync(writing: false, async cancellationToken =>
        {
            ModifierCatalog catalog = await _controller.ImportAsync(
                _archivePath.Text,
                new Progress<string>(SetStatus),
                cancellationToken);
            snapshot = catalog.SnapshotId;
            PopulateColors();
            PopulateCatalogNotices();
            PopulateModifiers();
            PopulateHistory();
            SetStatus(string.Format(_text["imported"], catalog.Items.Count));
        });
        if (snapshot is not null && !_closePending)
            _ = CompareSnapshotAsync(snapshot);
    }

    private async Task CompareAsync()
    {
        if (_controller.Catalog is null)
        {
            SetStatus(_text["empty"]);
            return;
        }
        await CompareSnapshotAsync(_controller.Catalog.SnapshotId);
    }

    private async Task CompareSnapshotAsync(string expectedSnapshot)
    {
        _comparisonCancellation?.Cancel();
        _comparisonCancellation?.Dispose();
        var request = new CancellationTokenSource();
        _comparisonCancellation = request;
        CancellationToken cancellationToken = request.Token;
        _compare.Enabled = false;
        SetStatus(_text["comparing"]);
        try
        {
            ComparisonReport report = await _controller.CompareAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested || IsDisposed || Disposing)
                return;
            if (report.SnapshotId != expectedSnapshot || _controller.Catalog?.SnapshotId != expectedSnapshot)
                return;
            PopulateComparison();
            PopulateModifiers();
            SetStatus(_text["compared"]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsDisposed && !Disposing)
            {
                string notice = string.Format(_text["comparison.failed"], exception.Message);
                SetNotices(_comparisonNoticesGroup, _comparisonNotices, [notice]);
                SetStatus(notice);
            }
        }
        finally
        {
            if (!IsDisposed && !Disposing && ReferenceEquals(_comparisonCancellation, request))
                _compare.Enabled = !_busy;
        }
    }

    private async Task ApplyAsync(IReadOnlyCollection<string> keys)
    {
        if (!EnsureArchivePath())
            return;
        if (keys.Count == 0)
        {
            SetStatus(_text["noRules"]);
            return;
        }
        await RunExclusiveAsync(writing: true, async cancellationToken =>
        {
            OperationResult result = await _controller.ApplyAsync(keys, new Progress<string>(SetStatus), cancellationToken);
            PopulateHistory();
            PopulateModifiers();
            ShowResult(result);
        });
    }

    private async Task UndoAsync(IReadOnlyCollection<string> changeIds)
    {
        if (!EnsureArchivePath())
            return;
        if (changeIds.Count == 0)
        {
            SetStatus(_text["noHistory"]);
            return;
        }
        await RunExclusiveAsync(writing: true, async cancellationToken =>
        {
            OperationResult result = await _controller.UndoAsync(changeIds, new Progress<string>(SetStatus), cancellationToken);
            PopulateHistory();
            PopulateModifiers();
            ShowResult(result);
        });
    }

    private async Task RunExclusiveAsync(bool writing, Func<CancellationToken, Task> operation)
    {
        if (_busy)
            return;
        _busy = true;
        _writing = writing;
        _operationCancellation = new CancellationTokenSource();
        SetBusyState();
        SetStatus(_text["working"]);
        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException) when (_operationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            _writing = false;
            _busy = false;
            SetBusyState();
            if (_closePending && !IsDisposed)
                BeginInvoke(Close);
        }
    }

    private void SetBusyState()
    {
        foreach (Control control in _mutationControls)
            control.Enabled = !_busy;
        _compare.Enabled = !_busy && _controller.Catalog is not null;
    }

    private bool EnsureArchivePath()
    {
        if (!File.Exists(_archivePath.Text))
            ChooseArchive();
        if (!File.Exists(_archivePath.Text))
            return false;
        if (!string.Equals(_controller.Settings.ArchivePath, _archivePath.Text, StringComparison.OrdinalIgnoreCase))
        {
            _controller.Settings.ArchivePath = _archivePath.Text;
            if (!TrySaveSettings())
                return false;
        }
        return true;
    }

    private Modifier[] SelectedModifiers() => _modifierGrid.SelectedRows.Cast<DataGridViewRow>()
        .Select(row => row.Tag as Modifier)
        .Where(modifier => modifier is not null)
        .Cast<Modifier>()
        .DistinctBy(modifier => modifier.Key)
        .ToArray();

    private string[] SelectedModifierKeys() => SelectedModifiers()
        .Where(modifier => _controller.Settings.Rules.ContainsKey(modifier.Key))
        .Select(modifier => modifier.Key)
        .ToArray();

    private string[] ConfiguredModifierKeys() => _controller.Catalog?.Items
        .Where(modifier => _controller.Settings.Rules.ContainsKey(modifier.Key))
        .Select(modifier => modifier.Key)
        .ToArray() ?? [];

    private string[] SelectedChangeIds() => _historyGrid.SelectedRows.Cast<DataGridViewRow>()
        .Select(row => row.Tag as ChangeRecord)
        .Where(change => change is { Status: "applied", UndoOf: null })
        .Select(change => change!.Id)
        .Distinct()
        .ToArray();

    private string[] LatestBatchChangeIds()
    {
        ChangeRecord[] applied = _controller.History
            .Where(change => change.Status == "applied" && change.UndoOf is null)
            .ToArray();
        string? latestBatch = applied.OrderByDescending(change => change.CreatedAt).FirstOrDefault()?.BatchId;
        return latestBatch is null
            ? []
            : applied.Where(change => change.BatchId == latestBatch).Select(change => change.Id).ToArray();
    }

    private void ChangeLanguage()
    {
        if (_changingLanguage || _language.SelectedItem is not Choice language)
            return;
        _controller.Settings.Language = language.Code;
        TrySaveSettings();
        _text = UiText.For(language.Code);
        ApplyLanguage();
    }

    private void ChangeGameLanguage()
    {
        if (_changingGameLanguage || _gameLanguage.SelectedItem is not Choice language)
            return;
        _controller.Settings.GameLanguage = language.Code;
        TrySaveSettings();
        PopulateModifiers();
    }

    private void OpenComparisonSource(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0 ||
            _comparisonGrid.Columns[eventArgs.ColumnIndex].Name != "source" ||
            _comparisonGrid.Rows[eventArgs.RowIndex].Tag is not ComparisonItem item ||
            !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out Uri? source) ||
            (source.Scheme != Uri.UriSchemeHttp && source.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(source.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _comparisonCancellation?.Cancel();
        if (_busy)
        {
            eventArgs.Cancel = true;
            _closePending = true;
            if (_writing)
            {
                SetStatus(_text["closeWaiting"]);
            }
            else
            {
                _operationCancellation?.Cancel();
                SetStatus(_text["closeCancelling"]);
            }
            return;
        }
        _operationCancellation?.Cancel();
    }

    private bool TrySaveSettings()
    {
        try
        {
            _controller.SaveSettings();
            return true;
        }
        catch (Exception exception)
        {
            ShowError(exception);
            return false;
        }
    }

    private void ShowResult(OperationResult result)
    {
        string message = string.Format(_text["result"], result.Applied, result.Skipped);
        if (result.Notices.Count > 0)
            message += " " + result.Notices[0];
        SetStatus(message);
    }

    private void ShowError(Exception exception)
    {
        SetStatus(exception.Message);
        MessageBox.Show(this, exception.Message, _text["error"], MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetStatus(string message)
    {
        if (!IsDisposed)
            _status.Text = message;
    }
}
