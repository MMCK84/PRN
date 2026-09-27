using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BilderUmbenenner
{
    public class MainForm : Form
    {
        private const string DateDisplay = "dd.MM.yyyy HH:mm:ss";

        private readonly List<RenameItem> items = new List<RenameItem>();
        private List<KeyValuePair<string, string>> lastRenames;

        private readonly ListView list;
        private readonly Button btnRename, btnRemove, btnClear, btnUndo, btnAdd;
        private readonly Label lblHint, lblStatus;
        private readonly CheckBox chkExif;
        private Font boldFont;

        public MainForm()
        {
            Text = "Bilder-Umbenenner  (yyyymmdd_hhmm)";
            Width = 1150;
            Height = 600;
            MinimumSize = new Size(800, 400);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            Font = new Font("Segoe UI", 9F);

            lblHint = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Bilder oder Ordner hierher ziehen. Neuer Name = EXIF-Aufnahmedatum, sonst Erstell- oder Änderungsdatum (das frühere). Format yyyymmdd_hhmm.",
                BackColor = Color.FromArgb(235, 242, 250)
            };

            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                AllowDrop = true,
                HideSelection = false
            };
            list.Columns.Add("Aktueller Name", 230);
            list.Columns.Add("Erstellt", 150);
            list.Columns.Add("Geändert", 150);
            list.Columns.Add("EXIF-Aufnahme", 150);
            list.Columns.Add("Neuer Name", 190);
            list.Columns.Add("Status", 120);
            list.Columns.Add("Ordner", 300);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6),
                FlowDirection = FlowDirection.LeftToRight
            };
            btnAdd = MakeButton("Dateien hinzufügen...", AddFilesDialog);
            btnRename = MakeButton("Alle umbenennen", DoRename);
            btnRename.Font = new Font(Font, FontStyle.Bold);
            btnUndo = MakeButton("Rückgängig", DoUndo);
            btnRemove = MakeButton("Auswahl entfernen", RemoveSelected);
            btnClear = MakeButton("Liste leeren", ClearList);
            chkExif = new CheckBox
            {
                Text = "EXIF-Aufnahmedatum verwenden (falls vorhanden)",
                Checked = RenameItem.UseExif,
                AutoSize = true,
                Margin = new Padding(16, 8, 3, 3)
            };
            chkExif.CheckedChanged += (s, e) => { RenameItem.UseExif = chkExif.Checked; RefreshList(); };
            buttons.Controls.AddRange(new Control[] { btnAdd, btnRename, btnUndo, btnRemove, btnClear, chkExif });

            lblStatus = new Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 3, 0, 0) };

            Controls.Add(list);
            Controls.Add(lblHint);
            Controls.Add(buttons);
            Controls.Add(lblStatus);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            list.DragEnter += OnDragEnter;
            list.DragDrop += OnDragDrop;
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };

            UpdateUi();
        }

        private Button MakeButton(string text, Action onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 30, Padding = new Padding(8, 0, 8, 0) };
            b.Click += (s, e) => onClick();
            return b;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) AddPaths(paths);
        }

        private void AddFilesDialog()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Multiselect = true;
                dlg.Title = "Bilder auswählen";
                dlg.Filter = "Bilder|" + string.Join(";", RenameEngine.ImageExtensions.Select(x => "*" + x).ToArray()) + "|Alle Dateien|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths(dlg.FileNames);
            }
        }

        public void AddPaths(IEnumerable<string> paths)
        {
            var known = new HashSet<string>(items.Select(i => i.CurrentPath), StringComparer.OrdinalIgnoreCase);
            int skipped = 0;
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var p in paths)
                {
                    IEnumerable<string> files;
                    if (Directory.Exists(p))
                    {
                        try { files = Directory.GetFiles(p, "*", SearchOption.AllDirectories); }
                        catch (Exception ex) { MessageBox.Show(this, p + "\n" + ex.Message, "Ordner nicht lesbar"); continue; }
                    }
                    else files = new[] { p };

                    foreach (var f in files)
                    {
                        if (!File.Exists(f)) continue;
                        if (!RenameEngine.IsImage(f)) { skipped++; continue; }
                        string full = Path.GetFullPath(f);
                        if (!known.Add(full)) continue;
                        try { items.Add(RenameItem.FromFile(full)); }
                        catch { skipped++; }
                    }
                }
            }
            finally { Cursor = Cursors.Default; }

            RefreshList();
            if (skipped > 0)
                lblStatus.Text += "   (" + skipped + " Nicht-Bild-Datei(en) ignoriert)";
        }

        private void RefreshList()
        {
            RenameEngine.ComputeNewNames(items);
            if (boldFont == null) boldFont = new Font(list.Font, FontStyle.Bold);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var it in items.OrderBy(i => Path.GetDirectoryName(i.CurrentPath)).ThenBy(i => i.NameDate))
            {
                var lvi = new ListViewItem(Path.GetFileName(it.CurrentPath)) { Tag = it };
                lvi.SubItems.Add(it.Created.ToString(DateDisplay));
                lvi.SubItems.Add(it.Modified.ToString(DateDisplay));
                lvi.SubItems.Add(it.ExifDate.HasValue ? it.ExifDate.Value.ToString(DateDisplay) : "–");
                lvi.SubItems.Add(it.NewName);
                lvi.SubItems.Add(it.Status);
                lvi.SubItems.Add(Path.GetDirectoryName(it.CurrentPath));
                // highlight the date that is used for the new name
                lvi.UseItemStyleForSubItems = false;
                var used = it.UsesExif ? lvi.SubItems[3] : it.Created <= it.Modified ? lvi.SubItems[1] : lvi.SubItems[2];
                used.Font = boldFont;
                if (it.Status.StartsWith("Fehler")) lvi.SubItems[5].ForeColor = Color.Red;
                else if (it.Status == "OK") lvi.SubItems[5].ForeColor = Color.Green;
                list.Items.Add(lvi);
            }
            list.EndUpdate();
            UpdateUi();
        }

        private void UpdateUi()
        {
            int toRename = items.Count(i => i.NewName != null && !string.Equals(Path.GetFileName(i.CurrentPath), i.NewName, StringComparison.Ordinal));
            int exif = items.Count(i => i.ExifDate.HasValue);
            lblStatus.Text = items.Count + " Bild(er) in der Liste (" + exif + " mit EXIF-Datum), " + toRename + " werden umbenannt.";
            btnRename.Enabled = toRename > 0;
            btnUndo.Enabled = lastRenames != null && lastRenames.Count > 0;
            btnRemove.Enabled = items.Count > 0;
            btnClear.Enabled = items.Count > 0;
        }

        private void RemoveSelected()
        {
            foreach (ListViewItem lvi in list.SelectedItems) items.Remove((RenameItem)lvi.Tag);
            RefreshList();
        }

        private void ClearList()
        {
            items.Clear();
            lastRenames = null;
            RefreshList();
        }

        private void DoRename()
        {
            int toRename = items.Count(i => !string.Equals(Path.GetFileName(i.CurrentPath), i.NewName, StringComparison.Ordinal));
            if (MessageBox.Show(this, toRename + " Datei(en) jetzt umbenennen?", "Umbenennen",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            // re-read dates in case files changed since they were added
            foreach (var it in items)
            {
                try { if (File.Exists(it.CurrentPath)) it.Reload(); }
                catch { }
                it.Status = "";
            }
            RenameEngine.ComputeNewNames(items);

            Cursor = Cursors.WaitCursor;
            try { lastRenames = RenameEngine.Apply(items); }
            finally { Cursor = Cursors.Default; }

            int errors = items.Count(i => i.Status.StartsWith("Fehler"));
            RefreshList();
            lblStatus.Text = lastRenames.Count + " Datei(en) umbenannt" + (errors > 0 ? ", " + errors + " Fehler." : ".");
            if (errors > 0)
                MessageBox.Show(this, errors + " Datei(en) konnten nicht umbenannt werden. Details in der Spalte \"Status\".",
                    "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void DoUndo()
        {
            if (lastRenames == null || lastRenames.Count == 0) return;
            List<string> errors;
            int count = RenameEngine.Undo(lastRenames, out errors);

            var map = lastRenames.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
            {
                string orig;
                if (map.TryGetValue(it.CurrentPath, out orig) && File.Exists(orig))
                {
                    it.CurrentPath = orig;
                    it.Status = "Zurückgesetzt";
                }
            }
            lastRenames = null;
            RefreshList();
            lblStatus.Text = count + " Umbenennung(en) rückgängig gemacht.";
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join("\n", errors.ToArray()), "Fehler beim Rückgängigmachen",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm();
            // files dropped onto the .exe icon arrive as arguments
            if (args.Length > 0) form.Shown += (s, e) => form.AddPaths(args);
            Application.Run(form);
        }
    }
}
