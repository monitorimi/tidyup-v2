using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TidyUp
{
    public class MainForm : Form
    {
        enum Stage { Scanning, Ready, Cleaning, Done }

        const int CollapsedHeight = 350;
        const int ExpandedHeight = 600;

        static readonly Color Accent = Color.FromArgb(0, 120, 212);
        static readonly Color Muted = Color.FromArgb(110, 110, 110);
        static readonly Color Dark = Color.FromArgb(32, 32, 32);

        readonly List<CleanItem> _items;
        readonly ListView _list = new ListView();
        readonly Label _big = new Label();
        readonly Label _caption = new Label();
        readonly ProgressBar _progress = new ProgressBar();
        readonly Button _button = new Button();
        readonly LinkLabel _detailsLink = new LinkLabel();
        readonly FlowLayoutPanel _links = new FlowLayoutPanel();

        Stage _stage = Stage.Scanning;
        bool _updating;
        bool _detailsOpen;
        long _freed;
        int _skipped;

        float UiScale { get { return CurrentAutoScaleDimensions.Width / 96f; } }

        public MainForm()
        {
            _items = Cleaner.CreateItems(Cleaner.IsAdmin);
            BuildUi();
            Render();
            Shown += async (s, e) => await ScanAsync();
        }

        // ---------- UI ----------

        void BuildUi()
        {
            SuspendLayout();

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;
            Text = "TidyUp";
            ClientSize = new Size(420, CollapsedHeight);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));   // 0 brand
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));  // 1 big number
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));   // 2 caption
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));   // 3 progress
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));   // 4 button
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));   // 5 links
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 6 details list

            table.Controls.Add(new Label
            {
                Text = "TidyUp",
                Font = new Font("Segoe UI Semibold", 12F),
                ForeColor = Accent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = Padding.Empty
            }, 0, 0);

            _big.Dock = DockStyle.Fill;
            _big.Font = new Font("Segoe UI Semibold", 40F);
            _big.ForeColor = Dark;
            _big.TextAlign = ContentAlignment.BottomCenter;
            _big.Margin = Padding.Empty;
            table.Controls.Add(_big, 0, 1);

            _caption.Dock = DockStyle.Fill;
            _caption.Font = new Font("Segoe UI", 11F);
            _caption.ForeColor = Muted;
            _caption.TextAlign = ContentAlignment.TopCenter;
            _caption.Margin = Padding.Empty;
            table.Controls.Add(_caption, 0, 2);

            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;
            _progress.Size = new Size(240, 6);
            _progress.Anchor = AnchorStyles.None;
            table.Controls.Add(_progress, 0, 3);

            _button.Size = new Size(240, 52);
            _button.Anchor = AnchorStyles.None;
            _button.FlatStyle = FlatStyle.Flat;
            _button.FlatAppearance.BorderSize = 0;
            _button.BackColor = Accent;
            _button.ForeColor = Color.White;
            _button.Font = new Font("Segoe UI Semibold", 14F);
            _button.Cursor = Cursors.Hand;
            _button.Click += ButtonClicked;
            table.Controls.Add(_button, 0, 4);

            _links.AutoSize = true;
            _links.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _links.WrapContents = false;
            _links.FlowDirection = FlowDirection.LeftToRight;
            _links.Anchor = AnchorStyles.None;
            _links.Margin = Padding.Empty;

            StyleLink(_detailsLink, "Show details");
            _detailsLink.LinkClicked += (s, e) => ToggleDetails();
            _links.Controls.Add(_detailsLink);

            if (!Cleaner.IsAdmin)
            {
                var admin = new LinkLabel();
                StyleLink(admin, "Include system files");
                admin.LinkClicked += (s, e) => RestartAsAdmin();
                _links.Controls.Add(admin);
            }
            table.Controls.Add(_links, 0, 5);

            BuildList();
            table.Controls.Add(_list, 0, 6);

            Controls.Add(table);

            ResumeLayout(false);
            PerformLayout();
        }

        static void StyleLink(LinkLabel link, string text)
        {
            link.Text = text;
            link.AutoSize = true;
            link.LinkColor = Accent;
            link.ActiveLinkColor = Accent;
            link.Margin = new Padding(10, 6, 10, 6);
        }

        void BuildList()
        {
            _list.Dock = DockStyle.Fill;
            _list.Visible = false;
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.HeaderStyle = ColumnHeaderStyle.None;
            _list.BorderStyle = BorderStyle.None;
            _list.ShowItemToolTips = true;
            _list.MultiSelect = false;
            _list.Font = new Font("Segoe UI", 10F);
            _list.Margin = new Padding(0);
            _list.Columns.Add("Item", 240);
            _list.Columns.Add("Size", 100, HorizontalAlignment.Right);

            // Taller rows: a 1px-wide image list sets the row height.
            _list.SmallImageList = new ImageList { ImageSize = new Size(1, (int)(30 * UiScale)) };

            _list.Resize += (s, e) => FitColumns();
            _list.ItemChecked += (s, e) => { if (!_updating) Render(); };

            foreach (var item in _items)
            {
                var li = new ListViewItem(item.Name) { Tag = item, ToolTipText = item.Description };
                li.SubItems.Add("");
                _list.Items.Add(li);
            }
        }

        void FitColumns()
        {
            if (_list.Columns.Count < 2) return;
            int sizeWidth = (int)(_list.ClientSize.Width * 0.30);
            _list.Columns[1].Width = sizeWidth;
            _list.Columns[0].Width = _list.ClientSize.Width - sizeWidth - 4;
        }

        void ToggleDetails()
        {
            _detailsOpen = !_detailsOpen;
            _list.Visible = _detailsOpen;
            _detailsLink.Text = _detailsOpen ? "Hide details" : "Show details";
            int h = _detailsOpen ? ExpandedHeight : CollapsedHeight;
            ClientSize = new Size(ClientSize.Width, (int)(h * UiScale));
        }

        // ---------- what the screen shows ----------

        long SelectedSum()
        {
            long sum = 0;
            foreach (ListViewItem li in _list.Items)
                if (li.Checked) sum += ((CleanItem)li.Tag).Size;
            return sum;
        }

        void Render()
        {
            bool busy = _stage == Stage.Scanning || _stage == Stage.Cleaning;
            _progress.Visible = busy;
            _button.Visible = !busy;
            _links.Enabled = !busy;
            _list.Enabled = !busy;

            switch (_stage)
            {
                case Stage.Scanning:
                    _big.Text = "Checking";
                    _caption.Text = "Looking for files you don't need";
                    break;

                case Stage.Cleaning:
                    _big.Text = "Cleaning";
                    _caption.Text = "This only takes a moment";
                    break;

                case Stage.Ready:
                    long sum = SelectedSum();
                    if (sum > 0)
                    {
                        _big.Text = Cleaner.FormatSize(sum);
                        _caption.Text = "can be freed";
                        _button.Text = "Clean";
                    }
                    else
                    {
                        _big.Text = "All tidy";
                        _caption.Text = "Nothing to clean right now";
                        _button.Text = "Check again";
                    }
                    break;

                case Stage.Done:
                    _big.Text = _freed > 0 ? Cleaner.FormatSize(_freed) : "Done";
                    _caption.Text = (_freed > 0 ? "freed. " : "") +
                        (_skipped > 0 ? "A few files were in use and left alone." : "Your PC is tidier now.");
                    _button.Text = "Close";
                    break;
            }
        }

        // ---------- actions ----------

        async Task ScanCoreAsync()
        {
            _updating = true;
            foreach (ListViewItem li in _list.Items) li.SubItems[1].Text = "...";
            _updating = false;

            try { await Task.WhenAll(_items.Select(i => Task.Run(() => i.Scan()))); }
            catch { }

            _updating = true;
            foreach (ListViewItem li in _list.Items)
            {
                var item = (CleanItem)li.Tag;
                li.SubItems[1].Text = Cleaner.FormatSize(item.Size);
                li.Checked = item.Size > 0;
            }
            _updating = false;
        }

        async Task ScanAsync()
        {
            _stage = Stage.Scanning;
            Render();
            await ScanCoreAsync();
            _stage = Stage.Ready;
            Render();
        }

        async void ButtonClicked(object sender, EventArgs e)
        {
            if (_stage == Stage.Done) { Close(); return; }
            if (_stage != Stage.Ready) return;

            if (SelectedSum() == 0) { await ScanAsync(); return; }

            if (!License.IsActivated())
            {
                License.ShowActivationRequired(this);
                return;
            }

            var selected = _list.Items.Cast<ListViewItem>()
                .Where(li => li.Checked)
                .Select(li => (CleanItem)li.Tag)
                .ToList();

            // Emptying the Recycle Bin can't be undone, so ask once.
            var bin = selected.OfType<RecycleBinItem>().FirstOrDefault();
            if (bin != null)
            {
                var answer = MessageBox.Show(this,
                    "This will also empty your Recycle Bin (" + Cleaner.FormatSize(bin.Size) + ").\n\nContinue?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
            }

            _stage = Stage.Cleaning;
            Render();

            long freed = 0;
            int skipped = 0;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var item in selected)
                    {
                        freed += item.Clean();
                        skipped += item.Skipped;
                    }
                });
            }
            catch { }

            await ScanCoreAsync(); // refresh the numbers behind "Show details"

            _freed = freed;
            _skipped = skipped;
            _stage = Stage.Done;
            Render();
        }

        void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
                Application.Exit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The user declined the Windows permission prompt.
            }
        }
    }
}
