using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class LanguagePackCheckForm : Form
    {
        private readonly LanguageManager _language;

        public LanguagePackCheckForm(LanguageManager language, Font appFont)
        {
            _language = language;
            Text = L("Status.LanguagePacksTitle");
            ClientSize = new Size(660, 430);
            MinimumSize = new Size(560, 360);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18, 16, 18, 14);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = L("Status.LanguagePacksTitle");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            header.Controls.Add(title);
            Label desc = new Label();
            desc.Text = L("LanguageCheck.Description");
            desc.ForeColor = UiStyle.Muted;
            desc.Location = new Point(0, 27);
            desc.Size = new Size(600, 25);
            header.Controls.Add(desc);
            root.Controls.Add(header, 0, 0);

            FastDataGridView grid = new FastDataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToResizeColumns = true;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            DataGridViewTextBoxColumn nameColumn = NewColumn("Name", L("LanguageCheck.Language"), 230);
            DataGridViewTextBoxColumn codeColumn = NewColumn("Code", L("LanguageCheck.Code"), 120);
            DataGridViewTextBoxColumn translatedColumn = NewColumn("Translated", L("LanguageCheck.Translated"), 120);
            DataGridViewTextBoxColumn percentColumn = NewColumn("Percent", L("LanguageCheck.Completion"), 110);
            UiStyle.ConfigureFillColumn(nameColumn, 100F, 220);
            UiStyle.ConfigureShortColumn(codeColumn, 90);
            UiStyle.ConfigureShortColumn(translatedColumn, 100);
            UiStyle.ConfigureShortColumn(percentColumn, 90);
            grid.Columns.Add(nameColumn);
            grid.Columns.Add(codeColumn);
            grid.Columns.Add(translatedColumn);
            grid.Columns.Add(percentColumn);
            UiStyle.StyleGrid(grid);
            GridInteraction.Apply(grid, "LanguagePackCheck", _language, false);
            root.Controls.Add(grid, 0, 1);

            List<LanguagePackCheck> checks = _language.CheckPacks();
            foreach (LanguagePackCheck check in checks)
            {
                int row = grid.Rows.Add(check.Name, check.Code, check.Translated + " / " + check.Total, check.Percent + "%");
                if (check.Percent < 100)
                    grid.Rows[row].Cells["Percent"].Style.ForeColor = Color.FromArgb(234, 88, 12);
                else
                    grid.Rows[row].Cells["Percent"].Style.ForeColor = Color.FromArgb(22, 163, 74);
            }

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.WrapContents = false;
            footer.Padding = new Padding(0, 7, 0, 0);
            Button close = UiStyle.NewButton(L("Common.Close"), 88, false);
            close.Click += delegate { Close(); };
            footer.Controls.Add(close);
            root.Controls.Add(footer, 0, 2);
            UiStyle.RelayoutLocalizedTree(this);
        }

        private string L(string key) { return _language.Get(key); }
        private DataGridViewTextBoxColumn NewColumn(string name, string title, int width)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name; c.HeaderText = title; c.Width = width; c.SortMode = DataGridViewColumnSortMode.NotSortable; return c;
        }
    }
}
