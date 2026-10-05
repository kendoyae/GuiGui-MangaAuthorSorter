using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class FileTypeProfileManagerForm : Form
    {
        private readonly FileTypeProfileStore _store;
        private readonly LanguageManager _language;
        private FileTypeProfileConfig _working;
        private readonly ListBox _profiles;
        private readonly ListBox _extensions;
        private readonly Label _detail;
        private readonly TextBox _addBox;
        private Button _renameProfileButton;
        private Button _duplicateProfileButton;
        private Button _deleteProfileButton;
        private Button _restoreProfileButton;
        private Button _removeExtensionButton;
        private Button _clearExtensionsButton;
        private Button _restoreExtensionsButton;
        private Button _addExtensionButton;
        private Button _saveButton;

        public FileTypeProfileConfig ResultConfig { get; private set; }

        public FileTypeProfileManagerForm(FileTypeProfileStore store, FileTypeProfileConfig source, LanguageManager language, Font appFont)
        {
            _store = store;
            _language = language;
            _working = _store.CloneConfig(source);

            Text = L("Dialog.FileType.Title");
            ClientSize = new Size(940, 590);
            MinimumSize = new Size(820, 540);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18, 16, 18, 14);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = L("Dialog.FileType.Title");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            header.Controls.Add(title);
            Label info = new Label();
            info.Text = L("Dialog.FileType.Info");
            info.ForeColor = UiStyle.Muted;
            info.Location = new Point(0, 27);
            info.AutoSize = true;
            info.MaximumSize = new Size(880, 0);
            info.AutoEllipsis = false;
            info.UseMnemonic = false;
            header.Controls.Add(info);
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 3;
            content.RowCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(content, 0, 1);

            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.Padding = new Padding(0, 0, 14, 0);
            left.ColumnCount = 1;
            left.RowCount = 3;
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));
            content.Controls.Add(left, 0, 0);

            Label profileTitle = new Label();
            profileTitle.Text = L("Dialog.FileType.ProfileGroup");
            profileTitle.Font = new Font(appFont, FontStyle.Bold);
            profileTitle.Dock = DockStyle.Fill;
            profileTitle.TextAlign = ContentAlignment.MiddleLeft;
            left.Controls.Add(profileTitle, 0, 0);

            _profiles = new ListBox();
            _profiles.Dock = DockStyle.Fill;
            _profiles.BorderStyle = BorderStyle.FixedSingle;
            _profiles.FormattingEnabled = true;
            _profiles.Format += delegate(object sender, ListControlConvertEventArgs e)
            {
                FileTypeProfile p = e.ListItem as FileTypeProfile;
                if (p != null) e.Value = DisplayName(p);
            };
            _profiles.SelectedIndexChanged += delegate { RenderExtensions(); UpdateActionButtons(); };
            left.Controls.Add(_profiles, 0, 1);

            TableLayoutPanel profileButtons = new TableLayoutPanel();
            profileButtons.Dock = DockStyle.Fill;
            profileButtons.Padding = new Padding(0, 8, 0, 0);
            profileButtons.ColumnCount = 2;
            profileButtons.RowCount = 3;
            profileButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            profileButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            profileButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            profileButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            profileButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            Button addProfile = FillButton(L("Dialog.FileType.New"), false, false);
            _renameProfileButton = FillButton(L("Dialog.FileType.Rename"), false, false);
            _duplicateProfileButton = FillButton(L("Dialog.FileType.Duplicate"), false, false);
            _deleteProfileButton = FillButton(L("Dialog.FileType.Delete"), false, true);
            _restoreProfileButton = FillButton(L("Dialog.FileType.RestoreSystem"), false, false);
            addProfile.Click += delegate { AddProfile(); };
            _renameProfileButton.Click += delegate { RenameProfile(); };
            _duplicateProfileButton.Click += delegate { DuplicateProfile(); };
            _deleteProfileButton.Click += delegate { DeleteProfile(); };
            _restoreProfileButton.Click += delegate { RestoreSystem(); };
            profileButtons.Controls.Add(addProfile, 0, 0);
            profileButtons.Controls.Add(_renameProfileButton, 1, 0);
            profileButtons.Controls.Add(_duplicateProfileButton, 0, 1);
            profileButtons.Controls.Add(_deleteProfileButton, 1, 1);
            profileButtons.Controls.Add(_restoreProfileButton, 0, 2);
            profileButtons.SetColumnSpan(_restoreProfileButton, 2);
            left.Controls.Add(profileButtons, 0, 2);

            Panel divider = new Panel();
            divider.Dock = DockStyle.Fill;
            divider.BackColor = UiStyle.Border;
            content.Controls.Add(divider, 1, 0);

            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(16, 0, 0, 0);
            right.ColumnCount = 1;
            right.RowCount = 4;
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));
            content.Controls.Add(right, 2, 0);

            Label extTitle = new Label();
            extTitle.Text = L("Dialog.FileType.ExtensionsGroup");
            extTitle.Font = new Font(appFont, FontStyle.Bold);
            extTitle.Dock = DockStyle.Fill;
            extTitle.TextAlign = ContentAlignment.MiddleLeft;
            right.Controls.Add(extTitle, 0, 0);

            _detail = new Label();
            _detail.ForeColor = UiStyle.Muted;
            _detail.Dock = DockStyle.Fill;
            _detail.TextAlign = ContentAlignment.MiddleLeft;
            _detail.AutoEllipsis = true;
            right.Controls.Add(_detail, 0, 1);

            TableLayoutPanel extArea = new TableLayoutPanel();
            extArea.Dock = DockStyle.Fill;
            extArea.ColumnCount = 2;
            extArea.RowCount = 1;
            extArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            extArea.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            extArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            right.Controls.Add(extArea, 0, 2);

            _extensions = new ListBox();
            _extensions.Dock = DockStyle.Fill;
            _extensions.BorderStyle = BorderStyle.FixedSingle;
            _extensions.SelectionMode = SelectionMode.MultiExtended;
            _extensions.SelectedIndexChanged += delegate { UpdateActionButtons(); };
            extArea.Controls.Add(_extensions, 0, 0);

            TableLayoutPanel extButtons = new TableLayoutPanel();
            extButtons.AutoSize = true;
            extButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            extButtons.Dock = DockStyle.Fill;
            extButtons.Padding = new Padding(12, 0, 0, 0);
            extButtons.ColumnCount = 1;
            extButtons.RowCount = 4;
            extButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            extButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            extButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            extButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            extButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _removeExtensionButton = UiStyle.NewButton(L("Common.DeleteSelected"), 0, false);
            _clearExtensionsButton = UiStyle.NewButton(L("Dialog.FileType.Clear"), 0, false);
            _restoreExtensionsButton = UiStyle.NewButton(L("Dialog.FileType.RestoreProfile"), 0, false);
            _removeExtensionButton.Dock = DockStyle.Fill;
            _clearExtensionsButton.Dock = DockStyle.Fill;
            _restoreExtensionsButton.Dock = DockStyle.Fill;
            _removeExtensionButton.Margin = new Padding(0, 0, 0, 8);
            _clearExtensionsButton.Margin = new Padding(0, 0, 0, 8);
            _restoreExtensionsButton.Margin = new Padding(0, 0, 0, 8);
            _removeExtensionButton.Click += delegate { RemoveExtensions(); };
            _clearExtensionsButton.Click += delegate { ClearExtensions(); };
            _restoreExtensionsButton.Click += delegate { RestoreCurrent(); };
            extButtons.Controls.Add(_removeExtensionButton, 0, 0);
            extButtons.Controls.Add(_clearExtensionsButton, 0, 1);
            extButtons.Controls.Add(_restoreExtensionsButton, 0, 2);
            extArea.Controls.Add(extButtons, 1, 0);

            TableLayoutPanel addPanel = new TableLayoutPanel();
            addPanel.Dock = DockStyle.Fill;
            addPanel.Padding = new Padding(0, 10, 0, 0);
            addPanel.ColumnCount = 3;
            addPanel.RowCount = 2;
            addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            addPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            addPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label addLabel = UiStyle.NewCaption(L("Dialog.FileType.AddExtension"));
            addLabel.Dock = DockStyle.Fill;
            addLabel.TextAlign = ContentAlignment.MiddleLeft;
            addPanel.Controls.Add(addLabel, 0, 0);
            _addBox = new TextBox();
            _addBox.Dock = DockStyle.Fill;
            _addBox.Margin = new Padding(0, 4, 8, 4);
            _addBox.TextChanged += delegate { UpdateActionButtons(); };
            _addBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { AddExtension(); e.SuppressKeyPress = true; }
            };
            addPanel.Controls.Add(_addBox, 1, 0);
            _addExtensionButton = UiStyle.NewButton(L("Dialog.FileType.Add"), 0, false);
            _addExtensionButton.Dock = DockStyle.Fill;
            _addExtensionButton.Margin = new Padding(0, 1, 0, 1);
            _addExtensionButton.Click += delegate { AddExtension(); };
            addPanel.Controls.Add(_addExtensionButton, 2, 0);
            Label hint = new Label();
            hint.Text = L("Dialog.FileType.ExtensionHint");
            hint.ForeColor = UiStyle.Muted;
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.TopLeft;
            addPanel.Controls.Add(hint, 0, 1);
            addPanel.SetColumnSpan(hint, 3);
            right.Controls.Add(addPanel, 0, 3);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.WrapContents = false;
            footer.Padding = new Padding(0, 7, 0, 0);
            Button cancel = UiStyle.NewButton(L("Common.Cancel"), 88, false);
            cancel.DialogResult = DialogResult.Cancel;
            _saveButton = UiStyle.NewButton(L("Dialog.FileType.SaveUse"), 150, true);
            _saveButton.Click += delegate { Save(); };
            footer.Controls.Add(cancel);
            footer.Controls.Add(_saveButton);
            root.Controls.Add(footer, 0, 2);
            AcceptButton = _saveButton;
            CancelButton = cancel;

            RefreshProfiles(_working.CurrentProfileId);
            UpdateActionButtons();
            UiStyle.RelayoutLocalizedTree(this);
        }

        private string L(string key) { return _language.Get(key); }
        private string LF(string key, params object[] args) { return _language.Format(key, args); }

        private Button FillButton(string text, bool primary, bool danger)
        {
            Button b = UiStyle.NewButton(text, 96, primary);
            UiStyle.ApplyButtonStateStyle(b, primary, danger);
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0, 0, 8, 4);
            return b;
        }

        private string DisplayName(FileTypeProfile p)
        {
            if (p == null) return L("Common.NotSet");
            string raw = (p.Name ?? "").Trim();
            if (String.Equals(p.Id, FileTypeRules.DefaultArchiveProfileId, StringComparison.OrdinalIgnoreCase) &&
                (raw.Length == 0 || raw == "Archive files" || raw == "压缩文件" || raw == "压缩包模式"))
                return L("FileProfile.Archive");
            if (String.Equals(p.Id, FileTypeRules.DefaultVideoProfileId, StringComparison.OrdinalIgnoreCase) &&
                (raw.Length == 0 || raw == "Video files" || raw == "视频文件" || raw == "视频模式"))
                return L("FileProfile.Video");
            if (raw.Length == 0 || raw == "Unnamed profile" || raw == "未命名方案" || raw == "未命名模式")
                return L("FileProfile.Unnamed");
            return raw;
        }

        private FileTypeProfile Selected { get { return _profiles.SelectedItem as FileTypeProfile; } }

        private void RefreshProfiles(string selectId)
        {
            _profiles.BeginUpdate();
            _profiles.Items.Clear();
            int index = -1;
            for (int i = 0; i < _working.Profiles.Count; i++)
            {
                _profiles.Items.Add(_working.Profiles[i]);
                if (String.Equals(_working.Profiles[i].Id, selectId, StringComparison.OrdinalIgnoreCase)) index = i;
            }
            _profiles.EndUpdate();
            if (_profiles.Items.Count > 0) _profiles.SelectedIndex = index >= 0 ? index : 0;
            RenderExtensions();
        }

        private void RenderExtensions()
        {
            FileTypeProfile p = Selected;
            _extensions.Items.Clear();
            if (p == null) { _detail.Text = L("Dialog.FileType.NoEditable"); UpdateActionButtons(); return; }
            p.Extensions = FileTypeRules.NormalizeExtensions(p.Extensions);
            foreach (string ext in p.Extensions) _extensions.Items.Add(ext);
            _detail.Text = LF("Dialog.FileType.ProfileDetail", DisplayName(p), p.Id, p.Extensions.Count);
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            FileTypeProfile p = Selected;
            bool hasProfile = p != null;
            int extensionCount = hasProfile && p.Extensions != null ? p.Extensions.Count : 0;

            if (_renameProfileButton != null) _renameProfileButton.Enabled = hasProfile;
            if (_duplicateProfileButton != null) _duplicateProfileButton.Enabled = hasProfile;
            if (_deleteProfileButton != null) _deleteProfileButton.Enabled = hasProfile && _working.Profiles.Count > 1;
            if (_restoreProfileButton != null) _restoreProfileButton.Enabled = _working.Profiles.Count > 0;

            if (_removeExtensionButton != null)
                _removeExtensionButton.Enabled = hasProfile && _extensions.SelectedItems.Count > 0;
            if (_clearExtensionsButton != null)
                _clearExtensionsButton.Enabled = hasProfile && extensionCount > 0;
            if (_restoreExtensionsButton != null)
                _restoreExtensionsButton.Enabled = hasProfile && FileTypeRules.GetDefaultsForProfileId(p.Id).Count > 0;
            if (_addExtensionButton != null)
                _addExtensionButton.Enabled = hasProfile && !String.IsNullOrWhiteSpace(_addBox.Text);
            if (_saveButton != null)
                _saveButton.Enabled = hasProfile && extensionCount > 0;
        }

        private string Prompt(string title, string value)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = title;
                dlg.ClientSize = new Size(440, 136);
                UiStyle.ApplyDialog(dlg, Font);
                TextBox box = new TextBox();
                box.Location = new Point(18, 22);
                box.Size = new Size(404, 26);
                box.Text = value ?? "";
                dlg.Controls.Add(box);
                Button cancel = UiStyle.NewButton(L("Common.Cancel"), 88, false);
                cancel.Location = new Point(334, 78);
                cancel.DialogResult = DialogResult.Cancel;
                Button ok = UiStyle.NewButton(L("Common.Save"), 88, true);
                ok.Location = new Point(238, 78);
                ok.DialogResult = DialogResult.OK;
                dlg.Controls.Add(cancel);
                dlg.Controls.Add(ok);
                dlg.CancelButton = cancel;
                dlg.AcceptButton = ok;
                dlg.Shown += delegate { box.Focus(); box.SelectAll(); };
                return dlg.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }

        private bool DuplicateName(string name, FileTypeProfile except)
        {
            return _working.Profiles.Any(delegate(FileTypeProfile p)
            {
                return !Object.ReferenceEquals(p, except) && String.Equals(DisplayName(p), name, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void AddProfile()
        {
            string name = Prompt(L("Dialog.FileType.NewTitle"), L("Dialog.FileType.CustomDefault"));
            if (name == null) return;
            if (name.Length == 0) { UiMessageBox.Show(this, L("Dialog.FileType.EmptyName"), L("Dialog.FileType.CannotCreate"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (DuplicateName(name, null)) { UiMessageBox.Show(this, L("Dialog.FileType.DuplicateName"), L("Dialog.FileType.CannotCreate"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            FileTypeProfile p = new FileTypeProfile();
            p.Id = _store.CreateCustomProfileId(_working);
            p.Name = name;
            p.Extensions = new List<string>();
            _working.Profiles.Add(p);
            RefreshProfiles(p.Id);
        }

        private void RenameProfile()
        {
            FileTypeProfile p = Selected;
            if (p == null) return;
            string name = Prompt(L("Dialog.FileType.RenameTitle"), DisplayName(p));
            if (name == null) return;
            if (name.Length == 0) { UiMessageBox.Show(this, L("Dialog.FileType.EmptyName"), L("Dialog.FileType.CannotRename"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (DuplicateName(name, p)) { UiMessageBox.Show(this, L("Dialog.FileType.DuplicateName"), L("Dialog.FileType.CannotRename"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            p.Name = name;
            RefreshProfiles(p.Id);
        }

        private void DuplicateProfile()
        {
            FileTypeProfile p = Selected;
            if (p == null) return;
            FileTypeProfile copy = new FileTypeProfile();
            copy.Id = _store.CreateCustomProfileId(_working);
            copy.Name = DisplayName(p) + L("Dialog.FileType.CopySuffix");
            copy.Extensions = FileTypeRules.NormalizeExtensions(p.Extensions);
            _working.Profiles.Add(copy);
            RefreshProfiles(copy.Id);
        }

        private void DeleteProfile()
        {
            FileTypeProfile p = Selected;
            if (p == null) return;
            if (_working.Profiles.Count <= 1) { UiMessageBox.Show(this, L("Dialog.FileType.KeepOne"), L("Dialog.FileType.CannotDelete"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (UiMessageBox.Show(this, LF("Dialog.FileType.DeleteConfirm", DisplayName(p)), L("Dialog.FileType.DeleteTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _working.Profiles.Remove(p);
            RefreshProfiles(_working.Profiles[0].Id);
        }

        private void RestoreSystem()
        {
            if (UiMessageBox.Show(this, L("Dialog.FileType.RestoreConfirm"), L("Dialog.FileType.RestoreTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string id = Selected != null ? Selected.Id : _working.CurrentProfileId;
            _working = _store.RestoreSystemPresets(_working);
            RefreshProfiles(id);
        }

        private void AddExtension()
        {
            FileTypeProfile p = Selected;
            if (p == null) return;
            string ext, error;
            if (!FileTypeRules.TryNormalizeExtension(_addBox.Text, out ext, out error)) { UiMessageBox.Show(this, _language.TranslateSource(error), L("Dialog.FileType.InvalidExtension"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!p.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) p.Extensions.Add(ext);
            p.Extensions = FileTypeRules.NormalizeExtensions(p.Extensions);
            _addBox.Clear();
            RenderExtensions();
        }

        private void RemoveExtensions()
        {
            FileTypeProfile p = Selected;
            if (p == null) return;
            List<string> selected = _extensions.SelectedItems.Cast<string>().ToList();
            p.Extensions = p.Extensions.Where(delegate(string x) { return !selected.Contains(x, StringComparer.OrdinalIgnoreCase); }).ToList();
            RenderExtensions();
        }

        private void ClearExtensions()
        {
            FileTypeProfile p = Selected;
            if (p != null) { p.Extensions.Clear(); RenderExtensions(); }
        }

        private void RestoreCurrent()
        {
            FileTypeProfile p = Selected;
            if (p == null) return;
            List<string> defaults = FileTypeRules.GetDefaultsForProfileId(p.Id);
            if (defaults.Count > 0) { p.Extensions = defaults; RenderExtensions(); }
        }

        private void Save()
        {
            FileTypeProfile p = Selected;
            if (p == null) return;
            if (p.Extensions == null || p.Extensions.Count == 0)
            {
                UiMessageBox.Show(this, LF("Dialog.FileType.EmptyExtensions", DisplayName(p)), L("Dialog.FileType.CannotUse"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _working.CurrentProfileId = p.Id;
            ResultConfig = _store.CloneConfig(_working);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
