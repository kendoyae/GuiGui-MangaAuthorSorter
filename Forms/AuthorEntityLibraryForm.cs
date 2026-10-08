using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class AuthorEntityLibraryForm : Form
    {
        private sealed class EntityRow
        {
            public int Id;
            public string Type;
            public string Canonical;
            public string Roman;
            public bool Confirmed;
            public bool LegacyGroup;
        }
        private readonly AuthorEntityStore _store;
        private readonly LanguageManager _language;
        private readonly TextBox _search;
        private readonly Label _summary;
        private readonly Label _progressText;
        private readonly ProgressBar _progress;
        private readonly Timer _reloadTimer;
        private readonly TabControl _tabs;
        private readonly FastDataGridView _grid, _publicGrid, _relations, _conflicts, _overrides, _rules, _lookups;
        private readonly TextBox _publicSearch;
        private readonly Label _publicSummary;
        private readonly Button _publicPrevious, _publicNext;
        private int _publicPage;
        private const int PageSize = 300;
        private AuthorEntityDatabase _db = new AuthorEntityDatabase();
        private bool _loading;
        private List<AuthorLibraryConflict> _allConflicts = new List<AuthorLibraryConflict>();

        public AuthorEntityLibraryForm(AuthorEntityStore store, LanguageManager language, Font appFont)
        {
            _store = store; _language = language;
            Text = L("AuthorEntityLibrary.Title"); ClientSize = new Size(1120, 740); MinimumSize = new Size(880, 600);
            UiStyle.ApplyDialog(this, appFont);
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
            root.Controls.Add(new Label { Text = L("EntityManager.Description"), AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(1000, 0), Margin = new Padding(0, 0, 0, 10) }, 0, 0);
            TableLayoutPanel search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            search.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            search.Controls.Add(new Label { Text = L("Common.Search"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _search = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(8) }; _search.TextChanged += delegate { if (!_loading) { RenderEntities(); RenderConflicts(); } };
            search.Controls.Add(_search, 1, 0); _summary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true }; search.Controls.Add(_summary, 2, 0); root.Controls.Add(search, 0, 1);
            _tabs = new TabControl { Dock = DockStyle.Fill }; root.Controls.Add(_tabs, 0, 2);
            _grid = Grid(new[] { "EntityManager.Id", "AuthorEntityLibrary.EntityType", "AuthorEntityLibrary.Canonical", "AuthorEntityLibrary.Roman", "AuthorEntityLibrary.Aliases", "EntityManager.DisabledNames", "AuthorEntityLibrary.Source", "AuthorEntityLibrary.Verified" });
            AddTab("EntityManager.UserTab", _grid, new[] { Button("EntityManager.AddArtist", () => EditEntity(null, "Artist")), Button("EntityManager.AddGroup", () => EditEntity(null, "Group")), Button("EntityManager.Edit", () => EditEntity(Selected<EntityRow>(_grid), null)), Button("EntityManager.Delete", DeleteEntity), Button("EntityManager.Import", ImportData), Button("EntityManager.Export", ExportData) });
            _grid.CellDoubleClick += delegate { EditEntity(Selected<EntityRow>(_grid), null); };
            _publicGrid = Grid(new[] { "EntityManager.Id", "AuthorEntityLibrary.EntityType", "AuthorEntityLibrary.Canonical", "AuthorEntityLibrary.Roman", "AuthorEntityLibrary.Aliases", "EntityManager.Provider", "EntityManager.RelationsTab", "AuthorEntityLibrary.Source" });
            TabPage publicTab = new TabPage(L("EntityManager.PublicTab"));
            TableLayoutPanel publicRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            publicRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); publicRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); publicRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            FlowLayoutPanel publicTools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _publicSearch = new TextBox { Width = 260 }; publicTools.Controls.Add(_publicSearch); publicTools.Controls.Add(Button("Common.Search", () => { _publicPage = 0; ReloadPublic(); }));
            _publicPrevious = Button("EntityManager.Previous", () => { _publicPage = Math.Max(0, _publicPage - 1); ReloadPublic(); });
            _publicNext = Button("EntityManager.Next", () => { _publicPage++; ReloadPublic(); }); publicTools.Controls.Add(_publicPrevious); publicTools.Controls.Add(_publicNext);
            publicTools.Controls.Add(Button("EntityManager.Override", () => EditPublic(null, Selected<PublicAuthorIndexRow>(_publicGrid))));
            _publicSummary = new Label { AutoSize = true, Margin = new Padding(8) };
            publicRoot.Controls.Add(publicTools, 0, 0); publicRoot.Controls.Add(_publicGrid, 0, 1); publicRoot.Controls.Add(_publicSummary, 0, 2); publicTab.Controls.Add(publicRoot); _tabs.TabPages.Add(publicTab);
            _publicSearch.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _publicPage = 0; ReloadPublic(); } };
            _relations = Grid(new[] { "EntityManager.Artist", "EntityManager.Group", "AuthorEntityLibrary.Source", "AuthorEntityLibrary.Verified", "EntityManager.DisabledNames" });
            AddTab("EntityManager.RelationsTab", _relations, new[] { Button("EntityManager.Link", () => EditRelation(null)), Button("EntityManager.Edit", () => EditRelation(Selected<AuthorCircleRecord>(_relations))), Button("EntityManager.Unlink", () => { AuthorCircleRecord r = Selected<AuthorCircleRecord>(_relations); if (r != null && Confirm("EntityManager.UnlinkConfirm")) Run(delegate { if (String.IsNullOrEmpty(r.PublicOverrideId)) _store.RemoveRelationship(r.AuthorId, r.CircleId); else _store.RemovePublicRelationship(r.PublicOverrideId, r.CircleId); }); }) });
            _conflicts = Grid(new[] { "EntityManager.Kind", "AuthorEntityLibrary.Query", "AuthorEntityLibrary.EntityType", "EntityManager.Candidates", "AuthorEntityLibrary.Status" });
            AddTab("EntityManager.ConflictsTab", _conflicts, new[] { Button("EntityManager.Resolve", ResolveConflict) });
            _overrides = Grid(new[] { "EntityManager.Original", "EntityManager.Effective", "AuthorEntityLibrary.EntityType", "EntityManager.StableKey", "AuthorEntityLibrary.Aliases", "EntityManager.DisabledNames", "AuthorEntityLibrary.Status" });
            AddTab("EntityManager.OverridesTab", _overrides, new[] { Button("EntityManager.Edit", () => EditPublic(Selected<PublicEntityOverride>(_overrides), null)), Button("EntityManager.Rebind", RebindOverride), Button("EntityManager.Restore", () => { PublicEntityOverride r = Selected<PublicEntityOverride>(_overrides); if (r != null && Confirm("EntityManager.RestoreConfirm")) Run(() => _store.RemovePublicOverride(r.Id)); }) });
            _rules = Grid(new[] { "AuthorEntityLibrary.Canonical", "AuthorEntityLibrary.Aliases", "AuthorEntityLibrary.Status" });
            AddTab("EntityManager.RulesTab", _rules, new[] { Button("EntityManager.AddRule", () => EditRule(null)), Button("EntityManager.Edit", () => EditRule(Selected<LegacyAliasGroupRecord>(_rules))), Button("EntityManager.Delete", () => { var r = Selected<LegacyAliasGroupRecord>(_rules); if (r != null && Confirm("EntityManager.DeleteConfirm")) Run(() => _store.SaveFolderRule(r.Id, r.Canonical, r.Names, true)); }) });
            _lookups = Grid(new[] { "EntityManager.Provider", "AuthorEntityLibrary.Query", "AuthorEntityLibrary.Status", "EntityManager.Detail", "AuthorEntityLibrary.Updated" });
            AddTab("EntityManager.CacheTab", _lookups, new[] { Button("EntityManager.ClearCache", () => { if (Confirm("EntityManager.ClearCacheConfirm")) Run(() => _store.ClearLookupCache()); }) });
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            footer.Controls.Add(Button("AuthorEntityLibrary.Refresh", () => { _store.RefreshPublicDatabase(); Reload(); ReloadPublic(); })); footer.Controls.Add(Button("Common.Close", Close));
            _progressText = new Label { AutoSize = true, Margin = new Padding(10), Text = L("AuthorEntityLibrary.ProgressIdle") };
            _progress = new ProgressBar { Width = 140, Height = 20, Margin = new Padding(10) }; footer.Controls.Add(_progress); footer.Controls.Add(_progressText); root.Controls.Add(footer, 0, 3);
            _reloadTimer = new Timer { Interval = 300 }; _reloadTimer.Tick += delegate { _reloadTimer.Stop(); Reload(); };
            _store.Changed += StoreChanged;
            FormClosed += delegate { _store.Changed -= StoreChanged; _reloadTimer.Dispose(); };
            _tabs.SelectedIndexChanged += delegate { Run(delegate {
                if (_tabs.SelectedIndex == 1) ReloadPublic();
                else if (_tabs.SelectedIndex == 3) { _allConflicts = _store.GetConflicts(); RenderConflicts(); }
                else if (_tabs.SelectedIndex == 4) Reload();
            }, false); };
            Reload(); UiStyle.RelayoutLocalizedTree(this);
        }
        private Button Button(string key, Action action)
        {
            Button b = UiStyle.NewButton(L(key), 90, false); b.AutoSize = true;
            b.Click += delegate { Run(action, false); }; return b;
        }
        private FastDataGridView Grid(string[] columns)
        {
            FastDataGridView grid = new FastDataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            int i = 0; foreach (string key in columns) {
                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn { Name = "C" + i++, HeaderText = L(key), MinimumWidth = 70, FillWeight = key.Contains("Aliases") ? 180 : 100, SortMode = DataGridViewColumnSortMode.NotSortable };
                column.HeaderCell.ToolTipText = L(key);
                if (key == "EntityManager.Id" || key == "AuthorEntityLibrary.EntityType" || key == "AuthorEntityLibrary.Verified")
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
                grid.Columns.Add(column);
            }
            UiStyle.StyleGrid(grid); return grid;
        }
        private void AddTab(string key, FastDataGridView grid, Button[] buttons)
        {
            TabPage tab = new TabPage(L(key)); TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(grid, 0, 0); FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; tools.Controls.AddRange(buttons); panel.Controls.Add(tools, 0, 1); tab.Controls.Add(panel); _tabs.TabPages.Add(tab);
        }
        private static T Selected<T>(DataGridView grid) where T : class { return grid.SelectedRows.Count == 1 ? grid.SelectedRows[0].Tag as T : null; }
        private void Row(DataGridView grid, object tag, params object[] cells) { int i = grid.Rows.Add(cells); grid.Rows[i].Tag = tag; }
        private bool Confirm(string key) { return UiMessageBox.Show(this, L(key), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes; }
        private void Run(Action action, bool reload = true)
        {
            try { action(); if (reload) Reload(); }
            catch (Exception ex) { UiMessageBox.Show(this, L("EntityManager.OperationFailed") + Environment.NewLine + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void StoreChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new MethodInvoker(delegate { if (!IsDisposed) { _reloadTimer.Stop(); _reloadTimer.Start(); } })); } catch (InvalidOperationException) { }
        }
        private void Reload()
        {
            _loading = true;
            try
            {
                _db = _store.LoadForDisplay(); RenderEntities(); _relations.Rows.Clear(); _conflicts.Rows.Clear(); _overrides.Rows.Clear(); _rules.Rows.Clear(); _lookups.Rows.Clear();
                foreach (AuthorCircleRecord r in _db.AuthorCircles) Row(_relations, r, AuthorName(r.AuthorId), GroupName(r.CircleId), r.Source, Yes(r.UserConfirmed), Yes(r.Disabled));
                foreach (PublicEntityOverride o in _db.PublicOverrides) foreach (PublicUserGroupRelation g in o.UserGroups) Row(_relations, new AuthorCircleRecord { PublicOverrideId = o.Id, CircleId = g.CircleId, UserConfirmed = g.UserConfirmed, Disabled = g.Disabled }, (String.IsNullOrEmpty(o.CanonicalName) ? o.OriginalCanonicalName : o.CanonicalName) + " — " + L("EntityManager.PublicTab"), GroupName(g.CircleId), "User", Yes(g.UserConfirmed), Yes(g.Disabled));
                _allConflicts = _tabs.SelectedIndex == 3 ? _store.GetConflicts() : new List<AuthorLibraryConflict>(); RenderConflicts();
                Dictionary<int, PublicEntitySnapshot> publicRows = new Dictionary<int, PublicEntitySnapshot>();
                if (_db.PublicOverrides.Count > 0 && _tabs.SelectedIndex == 4) try { publicRows = _store.GetPublicSnapshots(); } catch (InvalidOperationException) { }
                foreach (PublicEntityOverride r in _db.PublicOverrides) {
                    int count = AuthorEntityStore.BindOverride(r, publicRows.Values).Count;
                    Row(_overrides, r, r.OriginalCanonicalName, String.IsNullOrEmpty(r.CanonicalName) ? r.OriginalCanonicalName : r.CanonicalName, Type(r.EntityType), String.IsNullOrEmpty(r.PublicEntityKey) ? L("EntityManager.VersionBound") : r.PublicEntityKey, String.Join(" | ", r.AddedAliases.ToArray()), String.Join(" | ", r.DisabledNames.ToArray()), Status(count != 1 ? "Missing" : r.Status));
                }
                foreach (LegacyAliasGroupRecord r in _db.LegacyAliasGroups.Where(x => x.AuthorId == 0)) Row(_rules, r, r.Canonical, String.Join(" | ", r.Names.ToArray()), Status(r.Status));
                foreach (AuthorLookupCacheRecord r in _db.LookupCache.OrderByDescending(x => x.CheckedUtc).Take(1000)) Row(_lookups, r, r.Provider, r.Query, r.Status, r.Detail, r.CheckedUtc);
                _summary.Text = LF("EntityManager.Summary", _db.Authors.Count, _db.Circles.Count, _db.Aliases.Count + _db.CircleAliases.Count);
            }
            finally { _loading = false; }
        }
        private void RenderConflicts()
        {
            if (_conflicts == null) return; _conflicts.Rows.Clear();
            foreach (var r in _allConflicts.Where(x => (x.Name ?? "").IndexOf(_search.Text, StringComparison.OrdinalIgnoreCase) >= 0).Take(500))
                Row(_conflicts, r, Kind(r.Kind), r.Name, Type(r.EntityType), String.Join(" | ", r.Candidates.Select(id => CandidateLabel(id, r.EntityType)).Concat(r.PublicCandidates.Select(id => L("EntityManager.PublicTab") + " [" + id + "]")).ToArray()), Status(r.Status));
        }
        private void RenderEntities()
        {
            if (_grid == null) return; _grid.Rows.Clear(); string query = (_search.Text ?? "").Trim();
            var aliases = _db.Aliases.ToLookup(x => x.AuthorId); var groupAliases = _db.CircleAliases.ToLookup(x => x.CircleId);
            foreach (AuthorEntityRecord a in _db.Authors) {
                List<AuthorAliasRecord> names = aliases[a.Id].ToList();
                EntityRow r = new EntityRow { Id = a.Id, Type = a.EntityType, LegacyGroup = a.EntityType == "Group", Canonical = a.CanonicalName, Roman = a.RomanName, Confirmed = a.UserConfirmed };
                AddEntityRow(r, names.Where(x => !x.Disabled).Select(x => x.Alias), names.Where(x => x.Disabled).Select(x => x.Alias), a.Source, query);
            }
            foreach (CircleEntityRecord a in _db.Circles) {
                List<CircleAliasRecord> names = groupAliases[a.Id].ToList();
                EntityRow r = new EntityRow { Id = a.Id, Type = "Group", Canonical = a.CanonicalName, Roman = a.RomanName, Confirmed = a.UserConfirmed };
                AddEntityRow(r, names.Where(x => !x.Disabled).Select(x => x.Alias), names.Where(x => x.Disabled).Select(x => x.Alias), a.Source, query);
            }
        }
        private void AddEntityRow(EntityRow r, IEnumerable<string> names, IEnumerable<string> disabled, string source, string query)
        {
            string aliases = String.Join(" | ", names.ToArray()), disabledNames = String.Join(" | ", disabled.ToArray());
            if (query.Length > 0 && (r.Canonical + " " + r.Roman + " " + aliases + " " + disabledNames + " " + source).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) return;
            Row(_grid, r, r.Id, Type(r.Type), r.Canonical, r.Roman, aliases, disabledNames, source, Yes(r.Confirmed));
        }
        private void ReloadPublic()
        {
            _publicGrid.Rows.Clear(); PublicAuthorIndexInfo info = _store.GetPublicDatabaseInfo();
            if (info == null || !info.Exists) { _publicSummary.Text = L("EntityManager.PublicMissing"); _publicPrevious.Enabled = _publicNext.Enabled = false; return; }
            if (!String.IsNullOrWhiteSpace(info.Error)) { _publicSummary.Text = L("EntityManager.OperationFailed") + " " + info.Error; return; }
            long count = _store.CountPublicDatabaseSearch(_publicSearch.Text); int pages = Math.Max(1, (int)Math.Ceiling(count / (double)PageSize));
            _publicPage = Math.Min(_publicPage, pages - 1);
            foreach (PublicAuthorIndexRow r in _store.SearchPublicDatabase(_publicSearch.Text, _publicPage * PageSize, PageSize)) Row(_publicGrid, r, r.Id, Type(r.EntityType), r.CanonicalName, r.RomanName, r.Aliases, r.ProviderTags, r.RelatedGroups, r.Source);
            _publicSummary.Text = LF("EntityManager.PublicSummary", info.Artists, info.Groups, count, _publicPage + 1, pages);
            _publicPrevious.Enabled = _publicPage > 0; _publicNext.Enabled = _publicPage + 1 < pages;
        }
        private void EditEntity(EntityRow row, string newType)
        {
            if (row == null && newType == null) return;
            string type = row == null ? newType : row.Type;
            IEnumerable<string> aliases = row == null ? new string[0] : type == "Artist" || row.LegacyGroup ? _db.Aliases.Where(x => x.AuthorId == row.Id && !x.Disabled).Select(x => x.Alias) : _db.CircleAliases.Where(x => x.CircleId == row.Id && !x.Disabled).Select(x => x.Alias);
            IEnumerable<string> disabled = row == null ? new string[0] : type == "Artist" || row.LegacyGroup ? _db.Aliases.Where(x => x.AuthorId == row.Id && x.Disabled).Select(x => x.Alias) : _db.CircleAliases.Where(x => x.CircleId == row.Id && x.Disabled).Select(x => x.Alias);
            string providerText = "";
            if (row != null && (type == "Artist" || row.LegacyGroup)) {
                AuthorEntityRecord entity = _db.Authors.First(x => x.Id == row.Id);
                providerText = String.Join(" | ", (entity.ProviderIdentities ?? new List<string>()).Concat(new[] { entity.PublicEntityKey, entity.ExternalId, entity.EHArtistTag, entity.NHArtistTag }).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct().ToArray());
            } else if (row != null) {
                CircleEntityRecord entity = _db.Circles.First(x => x.Id == row.Id);
                providerText = String.Join(" | ", (entity.ProviderIdentities ?? new List<string>()).Concat(new[] { entity.PublicEntityKey, entity.EHGroupTag, entity.NHGroupTag }).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct().ToArray());
            }
            Dictionary<string, string> values = Editor(L("EntityManager.Edit") + " — " + Type(type), new[] { "AuthorEntityLibrary.Canonical", "AuthorEntityLibrary.Roman", "AuthorEntityLibrary.Aliases", "EntityManager.DisabledNames", "EntityManager.Provider" }, new[] { row == null ? "" : row.Canonical, row == null ? "" : row.Roman, String.Join(" | ", aliases.ToArray()), String.Join(" | ", disabled.ToArray()), providerText }, true, row == null || row.Confirmed);
            if (values == null) return; if (row != null && row.Confirmed && !Confirm("EntityManager.EditConfirmed")) return;
            Run(delegate {
                if (row != null && row.LegacyGroup) _store.SaveLegacyGroup(row.Id, values["AuthorEntityLibrary.Canonical"], values["AuthorEntityLibrary.Roman"], Split(values["AuthorEntityLibrary.Aliases"]), Split(values["EntityManager.DisabledNames"]), values["confirmed"] == "true");
                else _store.SaveUserEntity(row == null ? 0 : row.Id, type, values["AuthorEntityLibrary.Canonical"], values["AuthorEntityLibrary.Roman"], Split(values["AuthorEntityLibrary.Aliases"]), Split(values["EntityManager.DisabledNames"]), values["confirmed"] == "true");
            });
        }
        private void DeleteEntity()
        {
            EntityRow row = Selected<EntityRow>(_grid); if (row != null && Confirm("EntityManager.DeleteConfirm")) Run(() => _store.DeleteUserEntity(row.Id, row.LegacyGroup ? "Artist" : row.Type));
        }
        private void EditRule(LegacyAliasGroupRecord r)
        {
            var values = Editor(L("EntityManager.RulesTab"), new[] { "AuthorEntityLibrary.Canonical", "AuthorEntityLibrary.Aliases" }, new[] { r == null ? "" : r.Canonical, r == null ? "" : String.Join(" | ", r.Names.ToArray()) }, false, false);
            if (values != null) Run(() => _store.SaveFolderRule(r == null ? "" : r.Id, values["AuthorEntityLibrary.Canonical"], Split(values["AuthorEntityLibrary.Aliases"]), false));
        }
        private Dictionary<string, string> Editor(string title, string[] keys, string[] values, bool confirmation, bool confirmed)
        {
            using (Form dlg = new Form { Text = title, ClientSize = new Size(690, 460), MinimumSize = new Size(600, 400) })
            {
                UiStyle.ApplyDialog(dlg, Font); TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = keys.Length * 2 + 2 };
                Dictionary<string, TextBox> boxes = new Dictionary<string, TextBox>(); int line = 0;
                ToolTip hints = new ToolTip(); dlg.Disposed += delegate { hints.Dispose(); };
                for (int i = 0; i < keys.Length; i++) {
                    root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.Controls.Add(new Label { Text = L(keys[i]), AutoSize = true }, 0, line++);
                    bool multi = keys[i].Contains("Aliases") || keys[i].Contains("Disabled");
                    root.RowStyles.Add(new RowStyle(multi ? SizeType.Percent : SizeType.Absolute, multi ? 50 : 34));
                    TextBox box = new TextBox { Dock = DockStyle.Fill, Text = values[i], Multiline = multi, ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None }; if (keys[i] == "EntityManager.Original" || keys[i] == "EntityManager.Provider") box.ReadOnly = true; if (multi) hints.SetToolTip(box, L("EntityManager.AliasInputHint")); boxes[keys[i]] = box; root.Controls.Add(box, 0, line++);
                }
                CheckBox check = new CheckBox { Text = L("EntityManager.Confirmed"), AutoSize = true, Checked = confirmed, Visible = confirmation };
                check.AccessibleDescription = L("EntityManager.AliasInputHint");
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.Controls.Add(check, 0, line++);
                FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = UiStyle.NewButton(L("Common.Cancel"), 100, false); cancel.DialogResult = DialogResult.Cancel;
                Button save = UiStyle.NewButton(L("Common.Save"), 100, true);
                save.Click += delegate { if (boxes.ContainsKey("AuthorEntityLibrary.Canonical") && String.IsNullOrWhiteSpace(boxes["AuthorEntityLibrary.Canonical"].Text)) { UiMessageBox.Show(dlg, L("EntityManager.NameRequired"), title, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } dlg.DialogResult = DialogResult.OK; };
                tools.Controls.Add(cancel); tools.Controls.Add(save); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.Controls.Add(tools, 0, line); dlg.Controls.Add(root); dlg.CancelButton = cancel;
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                Dictionary<string, string> output = boxes.ToDictionary(x => x.Key, x => x.Value.Text); output["confirmed"] = check.Checked ? "true" : "false"; return output;
            }
        }
        private void EditPublic(PublicEntityOverride record, PublicAuthorIndexRow row)
        {
            PublicEntitySnapshot snapshot = row != null ? _store.GetPublicSnapshot(row.Id) : null;
            if (record != null) { var bound = AuthorEntityStore.BindOverride(record, _store.GetPublicSnapshots().Values); if (bound.Count == 1) snapshot = bound[0]; }
            if (snapshot == null) { if (record != null) Rebind(record); return; }
            if (record == null) record = _db.PublicOverrides.FirstOrDefault(x => AuthorEntityStore.BindOverride(x, _store.GetPublicSnapshots().Values).Any(s => s.Id == snapshot.Id));
            var values = Editor(L("EntityManager.Override") + " — " + snapshot.CanonicalName, new[] { "EntityManager.Original", "AuthorEntityLibrary.Canonical", "AuthorEntityLibrary.Aliases", "EntityManager.DisabledNames" },
                new[] { snapshot.CanonicalName, record == null || String.IsNullOrEmpty(record.CanonicalName) ? snapshot.CanonicalName : record.CanonicalName, record == null ? "" : String.Join(" | ", record.AddedAliases.ToArray()), record == null ? "" : String.Join(" | ", record.DisabledNames.ToArray()) }, false, true);
            if (values != null) Run(() => _store.SavePublicOverride(snapshot, values["AuthorEntityLibrary.Canonical"], Split(values["AuthorEntityLibrary.Aliases"]), Split(values["EntityManager.DisabledNames"]), record == null ? "" : record.Id));
        }
        private void RebindOverride() { var r = Selected<PublicEntityOverride>(_overrides); if (r != null) Rebind(r); }
        private void Rebind(PublicEntityOverride record)
        {
            PublicEntitySnapshot snapshot = PickPublic(record.EntityType); if (snapshot == null || !Confirm("EntityManager.RebindConfirm")) return;
            Run(() => _store.SavePublicOverride(snapshot, record.CanonicalName, record.AddedAliases, record.DisabledNames, record.Id));
        }
        private PublicEntitySnapshot PickPublic(string type)
        {
            using (Form dlg = new Form { Text = L("EntityManager.Rebind"), ClientSize = new Size(700, 480), MinimumSize = new Size(600, 400) }) {
                UiStyle.ApplyDialog(dlg, Font); TextBox search = new TextBox { Dock = DockStyle.Top }; ListBox list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Value" };
                List<PublicEntitySnapshot> snapshots = _store.GetPublicSnapshots().Values.Where(x => x.EntityType == type).ToList();
                Action render = delegate { list.DataSource = snapshots.Where(x => (x.CanonicalName + " " + x.RomanName + " " + String.Join(" ", x.Aliases.ToArray())).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).Take(300).Select(x => new KeyValuePair<PublicEntitySnapshot, string>(x, x.CanonicalName + " [" + x.Id + "] " + x.StableKey)).ToList(); };
                search.TextChanged += delegate { render(); }; Button select = UiStyle.NewButton(L("Common.OK"), 100, true); select.Dock = DockStyle.Bottom; select.Click += delegate { if (list.SelectedItem != null) dlg.DialogResult = DialogResult.OK; }; dlg.Controls.Add(list); dlg.Controls.Add(search); dlg.Controls.Add(select); render();
                return dlg.ShowDialog(this) == DialogResult.OK ? ((KeyValuePair<PublicEntitySnapshot, string>)list.SelectedItem).Key : null;
            }
        }
        private void EditRelation(AuthorCircleRecord relation)
        {
            if (relation == null && ((_db.Authors.Count == 0 && !_db.PublicOverrides.Any(x => x.EntityType == "Artist")) || _db.Circles.Count == 0)) { UiMessageBox.Show(this, L("EntityManager.NeedEndpoints"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (Form dlg = new Form { Text = L("EntityManager.RelationsTab"), ClientSize = new Size(630, 360), MinimumSize = new Size(580, 340) }) {
                UiStyle.ApplyDialog(dlg, Font); FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false };
                ComboBox artists = new ComboBox { Width = 550, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Value", DataSource = _db.Authors.Where(x => x.EntityType == "Artist").Select(x => new KeyValuePair<string, string>("user:" + x.Id, CandidateLabel(x.Id, "Artist"))).Concat(_db.PublicOverrides.Where(x => x.EntityType == "Artist").Select(x => new KeyValuePair<string, string>("public:" + x.Id, (String.IsNullOrEmpty(x.CanonicalName) ? x.OriginalCanonicalName : x.CanonicalName) + " — " + L("EntityManager.PublicTab")))).ToList() };
                ComboBox groups = new ComboBox { Width = 550, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Value", DataSource = _db.Circles.Select(x => new KeyValuePair<int, string>(x.Id, CandidateLabel(x.Id, "Group"))).ToList() };
                if (relation != null) { artists.Enabled = groups.Enabled = false; artists.SelectedIndex = ((List<KeyValuePair<string, string>>)artists.DataSource).FindIndex(x => x.Key == (String.IsNullOrEmpty(relation.PublicOverrideId) ? "user:" + relation.AuthorId : "public:" + relation.PublicOverrideId)); groups.SelectedIndex = _db.Circles.FindIndex(x => x.Id == relation.CircleId); }
                CheckBox confirmed = new CheckBox { Text = L("EntityManager.Confirmed"), AutoSize = true, Checked = relation == null || relation.UserConfirmed };
                CheckBox disabled = new CheckBox { Text = L("EntityManager.DisableRelation"), AutoSize = true, Checked = relation != null && relation.Disabled };
                panel.Controls.Add(new Label { Text = L("EntityManager.Artist"), AutoSize = true }); panel.Controls.Add(artists); panel.Controls.Add(new Label { Text = L("EntityManager.Group"), AutoSize = true }); panel.Controls.Add(groups); panel.Controls.Add(confirmed); panel.Controls.Add(disabled);
                Button save = UiStyle.NewButton(L("Common.Save"), 100, true); save.Click += delegate { if (artists.SelectedItem != null && groups.SelectedItem != null) dlg.DialogResult = DialogResult.OK; }; panel.Controls.Add(save); dlg.Controls.Add(panel);
                if (dlg.ShowDialog(this) == DialogResult.OK) Run(delegate {
                    string artist = ((KeyValuePair<string, string>)artists.SelectedItem).Key;
                    int group = ((KeyValuePair<int, string>)groups.SelectedItem).Key;
                    if (artist.StartsWith("public:")) _store.SavePublicRelationship(artist.Substring(7), group, confirmed.Checked, disabled.Checked);
                    else _store.SaveRelationship(Int32.Parse(artist.Substring(5)), group, confirmed.Checked, disabled.Checked);
                });
            }
        }
        private void ResolveConflict()
        {
            var conflict = Selected<AuthorLibraryConflict>(_conflicts); if (conflict == null) return;
            if (conflict.Kind == "PublicIdentity") { var r = _db.PublicOverrides.First(x => x.Id == conflict.OverrideId); Rebind(r); return; }
            using (Form dlg = new Form { Text = L("EntityManager.Resolve"), ClientSize = new Size(650, 210), MinimumSize = new Size(580, 210) }) {
                UiStyle.ApplyDialog(dlg, Font); FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false };
                panel.Controls.Add(new Label { Text = conflict.Name, AutoSize = true });
                ComboBox candidates = new ComboBox { Width = 570, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Value", DataSource = conflict.Candidates.Select(id => new KeyValuePair<string, string>("user:" + id, CandidateLabel(id, conflict.EntityType))).Concat(conflict.PublicCandidates.Select(id => new KeyValuePair<string, string>("public:" + id, L("EntityManager.PublicTab") + " — " + (_store.GetPublicSnapshot(id) == null ? id.ToString() : _store.GetPublicSnapshot(id).CanonicalName) + " [" + id + "]"))).ToList() }; panel.Controls.Add(candidates);
                ComboBox status = new ComboBox { Width = 570, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Value", DataSource = new[] { "Pending", "Confirmed", "Independent", "Disabled" }.Select(x => new KeyValuePair<string, string>(x, Status(x))).ToList() }; panel.Controls.Add(status);
                Button save = UiStyle.NewButton(L("Common.Save"), 100, true); save.Click += delegate { dlg.DialogResult = DialogResult.OK; }; panel.Controls.Add(save); dlg.Controls.Add(panel);
                if (dlg.ShowDialog(this) == DialogResult.OK) Run(delegate {
                    string choice = candidates.SelectedItem == null ? "" : ((KeyValuePair<string, string>)candidates.SelectedItem).Key;
                    string selectedStatus = ((KeyValuePair<string, string>)status.SelectedItem).Key;
                    if (choice.StartsWith("public:") && selectedStatus == "Confirmed") _store.ConfirmPublicName(conflict.Name, _store.GetPublicSnapshot(Int32.Parse(choice.Substring(7))));
                    else _store.SetNameDecision(conflict.Name, conflict.EntityType, choice.StartsWith("user:") ? Int32.Parse(choice.Substring(5)) : 0, selectedStatus);
                });
            }
        }
        private void ImportData() { using (OpenFileDialog dlg = new OpenFileDialog { Filter = "JSON|*.json|TXT|*.txt" }) if (dlg.ShowDialog(this) == DialogResult.OK && Confirm("EntityManager.ImportConfirm")) Run(() => _store.ImportUserLibrary(dlg.FileName)); }
        private void ExportData() { using (SaveFileDialog dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "AuthorEntities-export.json" }) if (dlg.ShowDialog(this) == DialogResult.OK) _store.ExportUserLibrary(dlg.FileName); }
        private static IEnumerable<string> Split(string value) { return (value ?? "").Replace('\r', '|').Replace('\n', '|').Split('|'); }
        private string AuthorName(int id) { var a = _db.Authors.FirstOrDefault(x => x.Id == id); return a == null ? id.ToString() : a.CanonicalName; }
        private string GroupName(int id) { var a = _db.Circles.FirstOrDefault(x => x.Id == id); return a == null ? id.ToString() : a.CanonicalName; }
        private string CandidateLabel(int id, string type) { return (type == "Artist" ? AuthorName(id) : GroupName(id)) + " [" + id + "]"; }
        private string Yes(bool yes) { return L(yes ? "Common.Yes" : "Common.No"); }
        private string Type(string type) { string key = "AuthorEntityLibrary.EntityType." + type; return L(key); }
        private string Status(string status) { string key = "EntityManager.Status." + status; return L(key); }
        private string Kind(string kind) { string key = "EntityManager.Kind." + kind; return L(key); }
        public void UpdateOnlineProgress(int current, int total, string query) { if (IsDisposed) return; if (InvokeRequired) { try { BeginInvoke(new Action<int, int, string>(UpdateOnlineProgress), current, total, query); } catch (InvalidOperationException) { } return; } _progress.Value = Math.Max(0, Math.Min(100, current * 100 / Math.Max(1, total))); _progressText.Text = LF("AuthorEntityLibrary.Progress", current, total, query); }
        public void MarkOnlineProgressComplete() { ProgressDone("AuthorEntityLibrary.ProgressComplete", 100); }
        public void MarkOnlineProgressCanceled() { ProgressDone("AuthorEntityLibrary.ProgressCanceled", 0); }
        private void ProgressDone(string key, int value) { if (IsDisposed) return; if (InvokeRequired) { try { BeginInvoke(new Action<string, int>(ProgressDone), key, value); } catch (InvalidOperationException) { } return; } _progress.Value = value; _progressText.Text = L(key); Reload(); }
        private string L(string key) { return _language.Get(key); }
        private string LF(string key, params object[] args) { return _language.Format(key, args); }
    }
}
