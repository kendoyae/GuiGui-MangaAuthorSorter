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
        private readonly TextBox _conflictSearch;
        private readonly ComboBox _scope;
        private readonly Label _summary;
        private readonly Label _progressText;
        private readonly ProgressBar _progress;
        private readonly Timer _reloadTimer;
        private readonly Timer _searchTimer;
        private readonly TabControl _tabs;
        private readonly FastDataGridView _grid, _relations, _conflicts, _overrides, _lookups;
        private readonly Label _publicSummary;
        private readonly Button _publicPrevious, _publicNext;
        private readonly Label _maintenanceSummary;
        private readonly Button _deleteEntityButton;
        private int _publicPage;
        private const int PageSize = 200;
        private AuthorEntityDatabase _db = new AuthorEntityDatabase();
        private bool _loading;
        private List<PublicAuthorIndexRow> _publicRows = new List<PublicAuthorIndexRow>();
        private List<AuthorLibraryConflict> _allConflicts = new List<AuthorLibraryConflict>();
        private string _relationArtistKey = "";
        private int _relationGroupId;
        private bool _relationDialogOpen;
        private bool _overrideDialogOpen;
        private bool _cacheDialogOpen;

        public AuthorEntityLibraryForm(AuthorEntityStore store, LanguageManager language, Font appFont)
        {
            _store = store; _language = language;
            Text = L("AuthorEntityLibrary.Title"); ClientSize = new Size(1120, 740); MinimumSize = new Size(880, 600);
            UiStyle.ApplyDialog(this, appFont);
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
            root.Controls.Add(new Label { Text = L("EntityManager.Description"), AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(1000, 0), Margin = new Padding(0, 0, 0, 10) }, 0, 0);
            _tabs = new TabControl { Dock = DockStyle.Fill }; root.Controls.Add(_tabs, 0, 1);

            // A single entry point for user and public entities. Public rows are paged and searched on demand.
            TabPage entitiesTab = new TabPage(L("EntityManager.EntitiesTab"));
            TableLayoutPanel entitiesRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            entitiesRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            entitiesRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            entitiesRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            entitiesRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            FlowLayoutPanel entitySearch = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            entitySearch.Controls.Add(new Label { Text = L("Common.Search"), AutoSize = true, Margin = new Padding(4, 8, 6, 0) });
            _search = new TextBox { Width = 420, Margin = new Padding(0, 3, 12, 3), Anchor = AnchorStyles.Left };
            entitySearch.Controls.Add(_search);
            entitySearch.Controls.Add(new Label { Text = L("EntityManager.Scope"), AutoSize = true, Margin = new Padding(2, 8, 6, 0) });
            _scope = new ComboBox { Width = 158, DropDownStyle = ComboBoxStyle.DropDownList };
            _scope.Items.AddRange(new object[] { L("EntityManager.ScopeAll"), L("EntityManager.ScopeUser"), L("EntityManager.ScopePublic") });
            entitySearch.Controls.Add(_scope);
            _summary = new Label { AutoSize = true, Margin = new Padding(14, 8, 4, 0) }; entitySearch.Controls.Add(_summary);
            entitiesRoot.Controls.Add(entitySearch, 0, 0);
            _grid = Grid(new[] { "AuthorEntityLibrary.EntityType", "AuthorEntityLibrary.Canonical", "AuthorEntityLibrary.Roman", "AuthorEntityLibrary.Aliases", "AuthorEntityLibrary.Source", "EntityManager.Provider", "AuthorEntityLibrary.Verified" });
            entitiesRoot.Controls.Add(_grid, 0, 1);
            _grid.CellDoubleClick += delegate { ShowEntityDetails(); };
            // Fix the paging order in physical left-to-right positions, regardless of parent RTL settings:
            // [Previous] [Next] [public database statistics].
            TableLayoutPanel pageTools = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, RightToLeft = RightToLeft.No };
            pageTools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pageTools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pageTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _publicPrevious = Button("EntityManager.Previous", () => { _publicPage = Math.Max(0, _publicPage - 1); RefreshEntitySearch(); });
            _publicNext = Button("EntityManager.Next", () => { _publicPage++; RefreshEntitySearch(); });
            _publicSummary = new Label { AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
            pageTools.Controls.Add(_publicPrevious, 0, 0);
            pageTools.Controls.Add(_publicNext, 1, 0);
            pageTools.Controls.Add(_publicSummary, 2, 0);
            entitiesRoot.Controls.Add(pageTools, 0, 2);
            FlowLayoutPanel entityActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _deleteEntityButton = Button("EntityManager.Delete", DeleteEntity);
            entityActions.Controls.AddRange(new[] {
                Button("EntityManager.AddArtist", () => EditEntity(null, "Artist")),
                Button("EntityManager.AddGroup", () => EditEntity(null, "Group")),
                Button("EntityManager.Details", ShowEntityDetails),
                Button("EntityManager.Edit", EditSelectedEntity),
                Button("EntityManager.RelationsTab", () => ShowRelationsDialog(_grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag : null)),
                _deleteEntityButton });
            _grid.SelectionChanged += delegate { _deleteEntityButton.Enabled = Selected<EntityRow>(_grid) != null; };
            entitiesRoot.Controls.Add(entityActions, 0, 3); entitiesTab.Controls.Add(entitiesRoot); _tabs.TabPages.Add(entitiesTab);

            TabPage conflictsTab = new TabPage(L("EntityManager.ConflictsTab"));
            TableLayoutPanel conflictsRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            conflictsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); conflictsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); conflictsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _conflictSearch = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 4, 4, 8) };
            _conflictSearch.TextChanged += delegate { RenderConflicts(); };
            conflictsRoot.Controls.Add(_conflictSearch, 0, 0);
            _conflicts = Grid(new[] { "EntityManager.Kind", "AuthorEntityLibrary.Query", "AuthorEntityLibrary.EntityType", "EntityManager.Candidates", "AuthorEntityLibrary.Status" });
            conflictsRoot.Controls.Add(_conflicts, 0, 1);
            FlowLayoutPanel conflictActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            conflictActions.Controls.Add(Button("EntityManager.Resolve", ResolveConflict));
            conflictActions.Controls.Add(Button("AuthorEntityLibrary.Refresh", ReloadConflicts));
            conflictsRoot.Controls.Add(conflictActions, 0, 2); conflictsTab.Controls.Add(conflictsRoot); _tabs.TabPages.Add(conflictsTab);

            // Advanced tables are hosted only in their on-demand management dialogs.
            _relations = Grid(new[] { "EntityManager.Artist", "EntityManager.Group", "AuthorEntityLibrary.Source", "AuthorEntityLibrary.Verified", "EntityManager.DisabledNames" });
            _overrides = Grid(new[] { "EntityManager.Original", "EntityManager.Effective", "AuthorEntityLibrary.EntityType", "EntityManager.StableKey", "AuthorEntityLibrary.Aliases", "EntityManager.DisabledNames", "AuthorEntityLibrary.Status" });
            _lookups = Grid(new[] { "EntityManager.Provider", "AuthorEntityLibrary.Query", "AuthorEntityLibrary.Status", "EntityManager.Detail", "AuthorEntityLibrary.Updated" });
            TabPage maintenanceTab = new TabPage(L("EntityManager.MaintenanceTab"));
            TableLayoutPanel maintenanceRoot = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
            maintenanceRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); maintenanceRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); maintenanceRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _maintenanceSummary = new Label { AutoSize = true, MaximumSize = new Size(940, 0), Margin = new Padding(0, 0, 0, 22) };
            maintenanceRoot.Controls.Add(_maintenanceSummary, 0, 0);
            FlowLayoutPanel maintenanceButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            maintenanceButtons.Controls.AddRange(new[] {
                Button("EntityManager.Import", ImportData), Button("EntityManager.Export", ExportData),
                Button("EntityManager.OverridesTab", ShowOverridesDialog), Button("EntityManager.CacheTab", ShowCacheDialog),
                Button("AuthorEntityLibrary.Refresh", () => { _store.RefreshPublicDatabase(); _publicPage = 0; Reload(); RefreshEntitySearch(); }) });
            maintenanceRoot.Controls.Add(maintenanceButtons, 0, 1);
            maintenanceRoot.Controls.Add(new Label { Text = L("EntityManager.MaintenanceHint"), AutoSize = true, ForeColor = UiStyle.Muted, MaximumSize = new Size(840, 0), Margin = new Padding(0, 18, 0, 0) }, 0, 2);
            maintenanceTab.Controls.Add(maintenanceRoot); _tabs.TabPages.Add(maintenanceTab);

            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            footer.Controls.Add(Button("Common.Close", Close));
            _progressText = new Label { AutoSize = true, Margin = new Padding(10), Text = L("AuthorEntityLibrary.ProgressIdle") };
            _progress = new ProgressBar { Width = 140, Height = 20, Margin = new Padding(10) }; footer.Controls.Add(_progress); footer.Controls.Add(_progressText); root.Controls.Add(footer, 0, 2);
            _reloadTimer = new Timer { Interval = 300 }; _reloadTimer.Tick += delegate { _reloadTimer.Stop(); Reload(); RefreshEntitySearch(); };
            _searchTimer = new Timer { Interval = 350 }; _searchTimer.Tick += delegate { _searchTimer.Stop(); RefreshEntitySearch(); };
            _search.TextChanged += delegate { if (!_loading) { _publicPage = 0; _searchTimer.Stop(); _searchTimer.Start(); } };
            _scope.SelectedIndexChanged += delegate { if (!_loading) { _publicPage = 0; RefreshEntitySearch(); } };
            _store.Changed += StoreChanged;
            FormClosed += delegate { _store.Changed -= StoreChanged; _reloadTimer.Dispose(); _searchTimer.Dispose(); };
            _tabs.SelectedIndexChanged += delegate { if (_tabs.SelectedIndex == 1) Run(ReloadConflicts, false); else if (_tabs.SelectedIndex == 2) UpdateMaintenanceSummary(); };
            _scope.SelectedIndex = 0;
            Reload(); RefreshEntitySearch(); UiStyle.RelayoutLocalizedTree(this);
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
        private static T Selected<T>(DataGridView grid) where T : class { return grid.SelectedRows.Count == 1 ? grid.SelectedRows[0].Tag as T : null; }
        private void Row(DataGridView grid, object tag, params object[] cells) { int i = grid.Rows.Add(cells); grid.Rows[i].Tag = tag; }
        private bool Confirm(string key) { return UiMessageBox.Show(this, L(key), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes; }
        private void Run(Action action, bool reload = true)
        {
            try { action(); if (reload) { Reload(); RefreshEntitySearch(); } }
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
                _db = _store.LoadForDisplay();
                RenderEntities(); RenderRelationships();
                if (_tabs.SelectedIndex == 1) ReloadConflicts();
                if (_overrideDialogOpen) RenderOverrides();
                if (_cacheDialogOpen) RenderLookupCache();
                _summary.Text = LF("EntityManager.Summary", _db.Authors.Count, _db.Circles.Count, _db.Aliases.Count + _db.CircleAliases.Count);
                UpdateMaintenanceSummary();
            }
            finally { _loading = false; }
        }
        private void ReloadConflicts()
        {
            _allConflicts = _store.GetConflicts(); RenderConflicts();
        }
        private void RenderConflicts()
        {
            if (_conflicts == null) return; _conflicts.Rows.Clear();
            string search = _conflictSearch == null ? "" : (_conflictSearch.Text ?? "");
            foreach (var r in _allConflicts.Where(x => (x.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).Take(500))
                Row(_conflicts, r, Kind(r.Kind), r.Name, Type(r.EntityType), String.Join(" | ", r.Candidates.Select(id => CandidateLabel(id, r.EntityType)).Concat(r.PublicCandidates.Select(id => L("EntityManager.PublicTab") + " [" + id + "]")).ToArray()), Status(r.Status));
        }
        private void RenderRelationships()
        {
            _relations.Rows.Clear();
            foreach (AuthorCircleRecord r in _db.AuthorCircles) {
                if (_relationArtistKey.Length > 0 && _relationArtistKey != "user:" + r.AuthorId) continue;
                if (_relationGroupId > 0 && _relationGroupId != r.CircleId) continue;
                Row(_relations, r, AuthorName(r.AuthorId), GroupName(r.CircleId), r.Source, Yes(r.UserConfirmed), Yes(r.Disabled));
            }
            foreach (PublicEntityOverride o in _db.PublicOverrides) foreach (PublicUserGroupRelation g in o.UserGroups) {
                if (_relationArtistKey.Length > 0 && _relationArtistKey != "public:" + o.Id) continue;
                if (_relationGroupId > 0 && _relationGroupId != g.CircleId) continue;
                Row(_relations, new AuthorCircleRecord { PublicOverrideId = o.Id, CircleId = g.CircleId, UserConfirmed = g.UserConfirmed, Disabled = g.Disabled },
                    (String.IsNullOrEmpty(o.CanonicalName) ? o.OriginalCanonicalName : o.CanonicalName) + " — " + L("EntityManager.PublicTab"), GroupName(g.CircleId), "User", Yes(g.UserConfirmed), Yes(g.Disabled));
            }
        }
        private void RenderOverrides()
        {
            _overrides.Rows.Clear();
            Dictionary<int, PublicEntitySnapshot> publicRows = new Dictionary<int, PublicEntitySnapshot>();
            if (_db.PublicOverrides.Count > 0) try { publicRows = _store.GetPublicSnapshots(); } catch (InvalidOperationException) { }
            foreach (PublicEntityOverride r in _db.PublicOverrides) {
                int count = AuthorEntityStore.BindOverride(r, publicRows.Values).Count;
                Row(_overrides, r, r.OriginalCanonicalName, String.IsNullOrEmpty(r.CanonicalName) ? r.OriginalCanonicalName : r.CanonicalName,
                    Type(r.EntityType), String.IsNullOrEmpty(r.PublicEntityKey) ? L("EntityManager.VersionBound") : r.PublicEntityKey,
                    String.Join(" | ", r.AddedAliases.ToArray()), String.Join(" | ", r.DisabledNames.ToArray()), Status(count != 1 ? "Missing" : r.Status));
            }
        }
        private void RenderLookupCache()
        {
            _lookups.Rows.Clear();
            foreach (AuthorLookupCacheRecord r in _db.LookupCache.OrderByDescending(x => x.CheckedUtc).Take(1000))
                Row(_lookups, r, r.Provider, r.Query, r.Status, r.Detail, r.CheckedUtc);
        }
        private void RenderEntities()
        {
            if (_grid == null) return; _grid.Rows.Clear();
            string query = (_search.Text ?? "").Trim();
            if (_scope.SelectedIndex != 2) {
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
            if (_scope.SelectedIndex != 1) foreach (PublicAuthorIndexRow a in _publicRows)
                Row(_grid, a, Type(a.EntityType), a.CanonicalName, a.RomanName, a.Aliases,
                    L("EntityManager.ScopePublic"), a.ProviderTags, Verification(a.VerificationSource));
        }
        private void AddEntityRow(EntityRow r, IEnumerable<string> names, IEnumerable<string> disabled, string source, string query)
        {
            string aliases = String.Join(" | ", names.ToArray()), disabledNames = String.Join(" | ", disabled.ToArray());
            string provider = "";
            if (r.Type == "Artist" || r.LegacyGroup) {
                AuthorEntityRecord a = _db.Authors.FirstOrDefault(x => x.Id == r.Id);
                if (a != null) provider = String.Join(" | ", (a.ProviderIdentities ?? new List<string>()).Concat(new[] { a.PublicEntityKey, a.EHArtistTag, a.NHArtistTag }).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct().ToArray());
            } else {
                CircleEntityRecord c = _db.Circles.FirstOrDefault(x => x.Id == r.Id);
                if (c != null) provider = String.Join(" | ", (c.ProviderIdentities ?? new List<string>()).Concat(new[] { c.PublicEntityKey, c.EHGroupTag, c.NHGroupTag }).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct().ToArray());
            }
            if (query.Length > 0 && (r.Canonical + " " + r.Roman + " " + aliases + " " + disabledNames + " " + source + " " + provider).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) return;
            Row(_grid, r, Type(r.Type), r.Canonical, r.Roman, aliases, source, provider, Yes(r.Confirmed));
        }
        private void RefreshEntitySearch()
        {
            _publicRows.Clear();
            bool includesPublic = _scope.SelectedIndex == 2 || (_scope.SelectedIndex == 0 && !String.IsNullOrWhiteSpace(_search.Text));
            _publicPrevious.Visible = _publicNext.Visible = includesPublic;
            if (includesPublic) {
                try {
                    PublicAuthorIndexInfo info = _store.GetPublicDatabaseInfo();
                    if (info == null || !info.Exists) _publicSummary.Text = L("EntityManager.PublicMissing");
                    else if (!String.IsNullOrWhiteSpace(info.Error)) _publicSummary.Text = L("EntityManager.OperationFailed") + " " + info.Error;
                    else {
                        long count = _store.CountPublicDatabaseSearch(_search.Text);
                        int pages = Math.Max(1, (int)Math.Ceiling(count / (double)PageSize));
                        _publicPage = Math.Min(_publicPage, pages - 1);
                        _publicRows = _store.SearchPublicDatabase(_search.Text, _publicPage * PageSize, PageSize);
                        _publicSummary.Text = LF("EntityManager.PublicSummary", info.Artists, info.Groups, count, _publicPage + 1, pages);
                        _publicPrevious.Enabled = _publicPage > 0; _publicNext.Enabled = _publicPage + 1 < pages;
                    }
                } catch (Exception ex) { _publicSummary.Text = L("EntityManager.OperationFailed") + " " + ex.Message; _publicPrevious.Enabled = _publicNext.Enabled = false; }
            } else {
                _publicSummary.Text = _scope.SelectedIndex == 0 ? L("EntityManager.PublicSearchHint") : "";
                _publicPrevious.Enabled = _publicNext.Enabled = false;
            }
            RenderEntities();
        }
        private void UpdateMaintenanceSummary()
        {
            if (_maintenanceSummary == null) return;
            PublicAuthorIndexInfo info = _store.GetPublicDatabaseInfo();
            string publicState = info == null || !info.Exists ? L("EntityManager.PublicMissing") :
                (!String.IsNullOrWhiteSpace(info.Error) ? info.Error : LF("EntityManager.PublicCounts", info.Artists, info.Groups));
            _maintenanceSummary.Text = L("EntityManager.MaintenanceSummary") + Environment.NewLine + publicState + Environment.NewLine +
                LF("EntityManager.UserDataSummary", _db.Authors.Count, _db.Circles.Count, _db.PublicOverrides.Count, _db.LookupCache.Count);
        }
        private void EditSelectedEntity()
        {
            object item = _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag : null;
            EntityRow user = item as EntityRow;
            PublicAuthorIndexRow publicRow = item as PublicAuthorIndexRow;
            if (user != null) EditEntity(user, null);
            else if (publicRow != null) EditPublic(null, publicRow);
        }
        private PublicEntityOverride FindOverride(PublicEntitySnapshot snapshot)
        {
            if (snapshot == null) return null;
            return _db.PublicOverrides.FirstOrDefault(x => x.EntityType == snapshot.EntityType &&
                (!String.IsNullOrEmpty(x.PublicEntityKey) && x.PublicEntityKey == snapshot.StableKey ||
                 x.OriginalPublicId == snapshot.Id && x.DatabaseVersion == snapshot.DatabaseVersion));
        }
        private void ShowEntityDetails()
        {
            object item = _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag : null;
            EntityRow user = item as EntityRow;
            PublicAuthorIndexRow publicRow = item as PublicAuthorIndexRow;
            if (item == null) return;
            string canonical = user != null ? user.Canonical : publicRow.CanonicalName;
            string kind = user != null ? user.Type : publicRow.EntityType;
            List<string> lines = new List<string>();
            lines.Add(L("AuthorEntityLibrary.EntityType") + ": " + Type(kind));
            lines.Add(L("AuthorEntityLibrary.Canonical") + ": " + canonical);
            lines.Add(L("AuthorEntityLibrary.Roman") + ": " + (user != null ? user.Roman : publicRow.RomanName));
            lines.Add(L("AuthorEntityLibrary.Source") + ": " + (user != null ? L("EntityManager.ScopeUser") : L("EntityManager.ScopePublic")));
            PublicEntityOverride boundOverride = null;
            if (user != null) {
                AuthorEntityRecord authorSource = _db.Authors.FirstOrDefault(x => x.Id == user.Id && (user.Type == "Artist" || user.LegacyGroup));
                CircleEntityRecord circleSource = user.LegacyGroup ? null : _db.Circles.FirstOrDefault(x => x.Id == user.Id);
                string sourceName = authorSource != null ? authorSource.Source : (circleSource == null ? "User" : circleSource.Source);
                lines.Add(L("AuthorEntityLibrary.Source") + " (" + L("EntityManager.Provider") + "): " + sourceName);
                bool legacy = user.Type == "Artist" || user.LegacyGroup;
                IEnumerable<string> aliases = legacy ? _db.Aliases.Where(x => x.AuthorId == user.Id && !x.Disabled).Select(x => x.Alias) : _db.CircleAliases.Where(x => x.CircleId == user.Id && !x.Disabled).Select(x => x.Alias);
                IEnumerable<string> disabled = legacy ? _db.Aliases.Where(x => x.AuthorId == user.Id && x.Disabled).Select(x => x.Alias) : _db.CircleAliases.Where(x => x.CircleId == user.Id && x.Disabled).Select(x => x.Alias);
                lines.Add(L("AuthorEntityLibrary.Aliases") + ": " + String.Join(" | ", aliases.ToArray()));
                lines.Add(L("EntityManager.DisabledNames") + ": " + String.Join(" | ", disabled.ToArray()));
                lines.Add(L("EntityManager.Confirmed") + ": " + Yes(user.Confirmed));
                if (legacy) {
                    AuthorEntityRecord a = _db.Authors.FirstOrDefault(x => x.Id == user.Id);
                    if (a != null) {
                        lines.Add(L("AuthorEntityLibrary.VerificationSource") + ": " + a.VerificationSource);
                        lines.Add(L("EntityManager.Provider") + ": " + String.Join(" | ", (a.ProviderIdentities ?? new List<string>()).Concat(new[] { a.EHArtistTag, a.NHArtistTag, a.PublicEntityKey }).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct().ToArray()));
                    }
                } else {
                    CircleEntityRecord g = _db.Circles.FirstOrDefault(x => x.Id == user.Id);
                    if (g != null) lines.Add(L("EntityManager.Provider") + ": " + String.Join(" | ", (g.ProviderIdentities ?? new List<string>()).Concat(new[] { g.EHGroupTag, g.NHGroupTag, g.PublicEntityKey }).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct().ToArray()));
                }
            } else {
                lines.Add(L("AuthorEntityLibrary.Aliases") + ": " + publicRow.Aliases);
                lines.Add(L("EntityManager.Provider") + ": " + publicRow.ProviderTags);
                lines.Add(L("AuthorEntityLibrary.VerificationSource") + ": " + publicRow.VerificationSource);
                lines.Add(L("EntityManager.RelationsTab") + ": " + publicRow.RelatedGroups);
                try {
                    PublicEntitySnapshot snapshot = _store.GetPublicSnapshot(publicRow.Id);
                    boundOverride = FindOverride(snapshot);
                } catch (Exception) { /* A missing public snapshot must not prevent read-only viewing. */ }
                if (boundOverride != null) {
                    lines.Add(L("EntityManager.Effective") + ": " + (String.IsNullOrEmpty(boundOverride.CanonicalName) ? canonical : boundOverride.CanonicalName));
                    lines.Add(L("EntityManager.DisabledNames") + ": " + String.Join(" | ", boundOverride.DisabledNames.ToArray()));
                    lines.Add(L("AuthorEntityLibrary.Aliases") + " (+): " + String.Join(" | ", boundOverride.AddedAliases.ToArray()));
                }
            }
            if (user != null) {
                IEnumerable<string> groups = user.Type == "Artist" ? _db.AuthorCircles.Where(x => x.AuthorId == user.Id && !x.Disabled).Select(x => GroupName(x.CircleId)) :
                    _db.AuthorCircles.Where(x => x.CircleId == user.Id && !x.Disabled).Select(x => AuthorName(x.AuthorId));
                lines.Add(L("EntityManager.RelationsTab") + ": " + String.Join(" | ", groups.ToArray()));
            }
            using (Form dlg = new Form { Text = L("EntityManager.Details") + " — " + canonical, ClientSize = new Size(730, 495), MinimumSize = new Size(600, 400) }) {
                UiStyle.ApplyDialog(dlg, Font);
                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(new Label { AutoSize = true, Text = L("EntityManager.DetailsHint"), Margin = new Padding(0, 0, 0, 12) }, 0, 0);
                TextBox info = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = String.Join(Environment.NewLine + Environment.NewLine, lines.ToArray()) };
                layout.Controls.Add(info, 0, 1);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
                actions.Controls.Add(Button("EntityManager.Edit", () => { dlg.Close(); if (user != null) EditEntity(user, null); else EditPublic(null, publicRow); }));
                actions.Controls.Add(Button("EntityManager.RelationsTab", () => { dlg.Close(); ShowRelationsDialog(item); }));
                if (boundOverride != null) {
                    PublicEntityOverride record = boundOverride;
                    actions.Controls.Add(Button("EntityManager.Restore", () => { if (Confirm("EntityManager.RestoreConfirm")) { dlg.Close(); Run(() => _store.RemovePublicOverride(record.Id)); } }));
                }
                actions.Controls.Add(Button("Common.Close", dlg.Close));
                layout.Controls.Add(actions, 0, 2); dlg.Controls.Add(layout); dlg.ShowDialog(this);
            }
        }
        private void ShowAdvancedDialog(string title, FastDataGridView grid, Button[] actions, Action beforeShow)
        {
            using (Form dlg = new Form { Text = title, ClientSize = new Size(960, 575), MinimumSize = new Size(760, 470) }) {
                UiStyle.ApplyDialog(dlg, Font);
                TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 2, ColumnCount = 1 };
                panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
                tools.Controls.AddRange(actions); panel.Controls.Add(grid, 0, 0); panel.Controls.Add(tools, 0, 1);
                dlg.Controls.Add(panel);
                try { if (beforeShow != null) beforeShow(); dlg.ShowDialog(this); }
                finally { panel.Controls.Remove(grid); }
            }
        }
        private void ShowRelationsDialog(object selected)
        {
            EntityRow user = selected as EntityRow; PublicAuthorIndexRow publicRow = selected as PublicAuthorIndexRow;
            _relationArtistKey = ""; _relationGroupId = 0;
            if (user != null) {
                if (user.Type == "Artist") _relationArtistKey = "user:" + user.Id;
                else if (!user.LegacyGroup) _relationGroupId = user.Id;
            }
            if (publicRow != null && publicRow.EntityType == "Group") {
                UiMessageBox.Show(this, L("EntityManager.PublicGroupRelationsHint"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (publicRow != null && publicRow.EntityType == "Artist") {
                try {
                    PublicEntitySnapshot snapshot = _store.GetPublicSnapshot(publicRow.Id);
                    PublicEntityOverride record = FindOverride(snapshot);
                    _relationArtistKey = record == null ? "snapshot:" + publicRow.Id : "public:" + record.Id;
                } catch (Exception ex) { UiMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            _relationDialogOpen = true;
            try {
                ShowAdvancedDialog(L("EntityManager.RelationsTab"), _relations, new[] {
                    Button("EntityManager.Link", () => EditRelation(null, selected)),
                    Button("EntityManager.Edit", () => EditRelation(Selected<AuthorCircleRecord>(_relations), selected)),
                    Button("EntityManager.Unlink", () => {
                        AuthorCircleRecord r = Selected<AuthorCircleRecord>(_relations);
                        if (r != null && Confirm("EntityManager.UnlinkConfirm")) Run(() => {
                            if (String.IsNullOrEmpty(r.PublicOverrideId)) _store.RemoveRelationship(r.AuthorId, r.CircleId);
                            else _store.RemovePublicRelationship(r.PublicOverrideId, r.CircleId);
                        });
                    }) }, RenderRelationships);
            } finally { _relationDialogOpen = false; _relationArtistKey = ""; _relationGroupId = 0; }
        }
        private void ShowOverridesDialog()
        {
            _overrideDialogOpen = true;
            try {
                ShowAdvancedDialog(L("EntityManager.OverridesTab"), _overrides, new[] {
                    Button("EntityManager.Edit", () => EditPublic(Selected<PublicEntityOverride>(_overrides), null)),
                    Button("EntityManager.Rebind", RebindOverride),
                    Button("EntityManager.Restore", () => { PublicEntityOverride r = Selected<PublicEntityOverride>(_overrides); if (r != null && Confirm("EntityManager.RestoreConfirm")) Run(() => _store.RemovePublicOverride(r.Id)); })
                }, RenderOverrides);
            } finally { _overrideDialogOpen = false; }
        }
        private void ShowCacheDialog()
        {
            _cacheDialogOpen = true;
            try {
                ShowAdvancedDialog(L("EntityManager.CacheTab"), _lookups, new[] {
                    Button("EntityManager.ClearCache", () => { if (Confirm("EntityManager.ClearCacheConfirm")) Run(() => _store.ClearLookupCache()); })
                }, RenderLookupCache);
            } finally { _cacheDialogOpen = false; }
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
        private void EditRelation(AuthorCircleRecord relation, object target)
        {
            bool publicTarget = target is PublicAuthorIndexRow && ((PublicAuthorIndexRow)target).EntityType == "Artist";
            if (relation == null && ((_db.Authors.Count == 0 && !_db.PublicOverrides.Any(x => x.EntityType == "Artist") && !publicTarget) || _db.Circles.Count == 0)) { UiMessageBox.Show(this, L("EntityManager.NeedEndpoints"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (Form dlg = new Form { Text = L("EntityManager.RelationsTab"), ClientSize = new Size(630, 360), MinimumSize = new Size(580, 340) }) {
                UiStyle.ApplyDialog(dlg, Font); FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false };
                List<KeyValuePair<string, string>> artistOptions = _db.Authors.Where(x => x.EntityType == "Artist").Select(x => new KeyValuePair<string, string>("user:" + x.Id, CandidateLabel(x.Id, "Artist")))
                    .Concat(_db.PublicOverrides.Where(x => x.EntityType == "Artist").Select(x => new KeyValuePair<string, string>("public:" + x.Id, (String.IsNullOrEmpty(x.CanonicalName) ? x.OriginalCanonicalName : x.CanonicalName) + " — " + L("EntityManager.PublicTab")))).ToList();
                if (publicTarget && !artistOptions.Any(x => x.Key == _relationArtistKey)) {
                    PublicAuthorIndexRow publicAuthor = (PublicAuthorIndexRow)target;
                    artistOptions.Add(new KeyValuePair<string, string>("snapshot:" + publicAuthor.Id, publicAuthor.CanonicalName + " — " + L("EntityManager.ScopePublic")));
                }
                // Populate the item lists before assigning SelectedIndex.  A ComboBox bound to a
                // DataSource may have zero Items until it receives a BindingContext, so assigning
                // even index 0 to a yet-unparented control can throw ArgumentOutOfRangeException.
                ComboBox artists = new ComboBox { Width = 550, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Value" };
                ComboBox groups = new ComboBox { Width = 550, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Value" };
                artists.Items.AddRange(artistOptions.Cast<object>().ToArray());
                groups.Items.AddRange(_db.Circles.Select(x => (object)new KeyValuePair<int, string>(x.Id, CandidateLabel(x.Id, "Group"))).ToArray());
                if (relation != null) {
                    artists.Enabled = groups.Enabled = false;
                    int artistIndex = artistOptions.FindIndex(x => x.Key == (String.IsNullOrEmpty(relation.PublicOverrideId) ? "user:" + relation.AuthorId : "public:" + relation.PublicOverrideId));
                    int groupIndex = _db.Circles.FindIndex(x => x.Id == relation.CircleId);
                    if (artistIndex >= 0 && artistIndex < artists.Items.Count) artists.SelectedIndex = artistIndex;
                    if (groupIndex >= 0 && groupIndex < groups.Items.Count) groups.SelectedIndex = groupIndex;
                }
                else {
                    int artistIndex = _relationArtistKey.Length > 0 ? artistOptions.FindIndex(x => x.Key == _relationArtistKey) : 0;
                    int groupIndex = _relationGroupId > 0 ? _db.Circles.FindIndex(x => x.Id == _relationGroupId) : 0;
                    if (artistIndex >= 0 && artistIndex < artists.Items.Count) artists.SelectedIndex = artistIndex;
                    if (groupIndex >= 0 && groupIndex < groups.Items.Count) groups.SelectedIndex = groupIndex;
                }
                CheckBox confirmed = new CheckBox { Text = L("EntityManager.Confirmed"), AutoSize = true, Checked = relation == null || relation.UserConfirmed };
                CheckBox disabled = new CheckBox { Text = L("EntityManager.DisableRelation"), AutoSize = true, Checked = relation != null && relation.Disabled };
                panel.Controls.Add(new Label { Text = L("EntityManager.Artist"), AutoSize = true }); panel.Controls.Add(artists); panel.Controls.Add(new Label { Text = L("EntityManager.Group"), AutoSize = true }); panel.Controls.Add(groups); panel.Controls.Add(confirmed); panel.Controls.Add(disabled);
                Button save = UiStyle.NewButton(L("Common.Save"), 100, true); save.Click += delegate { if (artists.SelectedItem != null && groups.SelectedItem != null) dlg.DialogResult = DialogResult.OK; }; panel.Controls.Add(save); dlg.Controls.Add(panel);
                if (dlg.ShowDialog(this) == DialogResult.OK) Run(delegate {
                    string artist = ((KeyValuePair<string, string>)artists.SelectedItem).Key;
                    int group = ((KeyValuePair<int, string>)groups.SelectedItem).Key;
                    if (artist.StartsWith("snapshot:")) {
                        PublicEntitySnapshot snapshot = _store.GetPublicSnapshot(Int32.Parse(artist.Substring(9)));
                        _store.SavePublicOverride(snapshot, "", new string[0], new string[0], "");
                        _db = _store.LoadForDisplay();
                        PublicEntityOverride newOverride = FindOverride(snapshot);
                        if (newOverride == null) throw new InvalidOperationException("Public override was not saved.");
                        artist = "public:" + newOverride.Id;
                        _relationArtistKey = artist;
                    }
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
        private string Verification(string source)
        {
            if (String.IsNullOrWhiteSpace(source)) return "—";
            string key = "AuthorEntityLibrary.Verification." + source;
            string localized = _language.Get(key);
            return localized == key ? source : localized;
        }
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

namespace MangaAuthorSorter
{
    // Folder aliases are an archival naming rule, not an author identity.
    // Keep their storage unchanged, but expose them from Archive Settings.
    internal sealed class FolderNameRulesForm : Form
    {
        private readonly AuthorEntityStore _store;
        private readonly LanguageManager _language;
        private readonly FastDataGridView _grid;
        internal FolderNameRulesForm(AuthorEntityStore store, LanguageManager language, Font appFont)
        {
            _store = store; _language = language;
            Text = L("EntityManager.RulesTab"); ClientSize = new Size(800, 500); MinimumSize = new Size(630, 380);
            UiStyle.ApplyDialog(this, appFont);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = L("EntityManager.FolderRuleHint"), AutoSize = true, Margin = new Padding(0, 0, 0, 10) }, 0, 0);
            _grid = new FastDataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            _grid.Columns.Add("Canonical", L("AuthorEntityLibrary.Canonical"));
            _grid.Columns.Add("Aliases", L("AuthorEntityLibrary.Aliases"));
            _grid.Columns.Add("Status", L("AuthorEntityLibrary.Status"));
            UiStyle.StyleGrid(_grid); layout.Controls.Add(_grid, 0, 1);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            AddAction(actions, "EntityManager.AddRule", () => Edit(null));
            AddAction(actions, "EntityManager.Edit", () => Edit(SelectedRule()));
            AddAction(actions, "EntityManager.Delete", () => {
                LegacyAliasGroupRecord r = SelectedRule();
                if (r == null || UiMessageBox.Show(this, L("EntityManager.DeleteConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                Run(() => _store.SaveFolderRule(r.Id, r.Canonical, r.Names, true));
            });
            AddAction(actions, "AuthorEntityLibrary.Refresh", Reload);
            AddAction(actions, "Common.Close", Close);
            layout.Controls.Add(actions, 0, 2); Controls.Add(layout);
            _grid.CellDoubleClick += delegate { Edit(SelectedRule()); };
            Reload(); UiStyle.RelayoutLocalizedTree(this);
        }
        private void AddAction(Control parent, string key, Action action)
        {
            Button button = UiStyle.NewButton(L(key), 96, false); button.AutoSize = true;
            button.Click += delegate { action(); }; parent.Controls.Add(button);
        }
        private string L(string key) { return _language.Get(key); }
        private string RuleStatus(string status) { return _language.Get("EntityManager.Status." + status); }
        private LegacyAliasGroupRecord SelectedRule() { return _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as LegacyAliasGroupRecord : null; }
        private void Run(Action operation)
        {
            try { operation(); Reload(); }
            catch (Exception ex) { UiMessageBox.Show(this, L("EntityManager.OperationFailed") + Environment.NewLine + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void Reload()
        {
            _grid.Rows.Clear();
            foreach (LegacyAliasGroupRecord r in _store.LoadForDisplay().LegacyAliasGroups.Where(x => x.AuthorId == 0)) {
                int i = _grid.Rows.Add(r.Canonical, String.Join(" | ", r.Names.ToArray()), RuleStatus(r.Status));
                _grid.Rows[i].Tag = r;
            }
        }
        private void Edit(LegacyAliasGroupRecord record)
        {
            using (Form dlg = new Form { Text = L("EntityManager.RulesTab"), ClientSize = new Size(610, 285), MinimumSize = new Size(500, 260) }) {
                UiStyle.ApplyDialog(dlg, Font);
                TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                panel.Controls.Add(new Label { Text = L("AuthorEntityLibrary.Canonical"), AutoSize = true }, 0, 0);
                TextBox canonical = new TextBox { Dock = DockStyle.Fill, Text = record == null ? "" : record.Canonical };
                panel.Controls.Add(canonical, 0, 1);
                panel.Controls.Add(new Label { Text = L("AuthorEntityLibrary.Aliases") + " ( | )", AutoSize = true }, 0, 2);
                TextBox aliases = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = record == null ? "" : String.Join(" | ", record.Names.ToArray()) };
                panel.Controls.Add(aliases, 0, 3);
                panel.Controls.Add(new Label { Text = L("EntityManager.FolderRuleHint"), AutoSize = true, ForeColor = UiStyle.Muted }, 0, 4);
                FlowLayoutPanel controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
                Button save = UiStyle.NewButton(L("Common.Save"), 90, true);
                Button cancel = UiStyle.NewButton(L("Common.Cancel"), 90, false); cancel.DialogResult = DialogResult.Cancel;
                save.Click += delegate {
                    if (String.IsNullOrWhiteSpace(canonical.Text)) { UiMessageBox.Show(dlg, L("EntityManager.NameRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    dlg.DialogResult = DialogResult.OK;
                };
                controls.Controls.Add(cancel); controls.Controls.Add(save); panel.Controls.Add(controls, 0, 5);
                dlg.Controls.Add(panel); dlg.CancelButton = cancel;
                if (dlg.ShowDialog(this) == DialogResult.OK) Run(() => _store.SaveFolderRule(record == null ? "" : record.Id, canonical.Text,
                    aliases.Text.Replace('\r', '|').Replace('\n', '|').Split('|'), false));
            }
        }
    }
}
