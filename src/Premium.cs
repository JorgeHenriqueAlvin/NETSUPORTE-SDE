namespace SdeSuporte;

static class Premium
{
    public static readonly Color Canvas=Color.FromArgb(244,247,250);
    public static readonly Color Ink=Color.FromArgb(24,43,66);
    public static readonly Color Muted=Color.FromArgb(87,105,124);
    public static readonly Color Brand=Color.FromArgb(20,49,81);
    public static readonly Color Accent=Color.FromArgb(0,114,125);
    public static readonly Color Border=Color.FromArgb(218,226,234);
    public static void Primary(Button button)
    {
        button.BackColor=Accent;button.ForeColor=Color.White;
        button.FlatAppearance.BorderColor=Accent;
        button.FlatAppearance.MouseOverBackColor=Color.FromArgb(0,91,102);
        button.FlatAppearance.MouseDownBackColor=Brand;
    }
    public static void Apply(Control root)
    {
        if(root is not Label||root.Font.Size<=10)root.Font=new Font("Segoe UI",10);
        root.BackColor=Canvas;
        root.ForeColor=Ink;
        foreach(Control child in root.Controls)
        {
            Apply(child);
            if(child is Button button)
            {
                button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=1;
                button.FlatAppearance.BorderColor=Border;button.UseVisualStyleBackColor=false;
                button.BackColor=Color.White;
                button.Padding=new Padding(10,6,10,6);button.Margin=new Padding(4);
                button.Cursor=Cursors.Hand;button.MinimumSize=new Size(0,34);
                button.FlatAppearance.MouseOverBackColor=Color.FromArgb(230,242,244);
                button.FlatAppearance.MouseDownBackColor=Color.FromArgb(214,232,236);
                button.AccessibleName=string.IsNullOrEmpty(button.AccessibleName)?button.Text:button.AccessibleName;
            }
            if(child is TextBox or RichTextBox or ListBox)child.BackColor=Color.White;
            if(child is TextBox box){box.BorderStyle=BorderStyle.FixedSingle;box.Margin=new Padding(4,6,4,6);}
            if(child is ComboBox combo){combo.BackColor=Color.White;combo.FlatStyle=FlatStyle.Flat;}
            if(child is FlowLayoutPanel flow&&flow.Dock==DockStyle.Top)
            {
                flow.AutoSize=true;flow.AutoSizeMode=AutoSizeMode.GrowAndShrink;
                flow.WrapContents=true;flow.Padding=new Padding(6);flow.MinimumSize=new Size(0,48);
            }
            if(child is DataGridView grid)
            {
                grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;
                grid.EnableHeadersVisualStyles=false;grid.GridColor=Border;
                grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
                grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
                grid.ColumnHeadersDefaultCellStyle.BackColor=Brand;
                grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.White;
                grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI Semibold",10);
                grid.ColumnHeadersDefaultCellStyle.Padding=new Padding(8);
                grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
                grid.DefaultCellStyle.Padding=new Padding(8,6,8,6);
                grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(220,239,242);
                grid.DefaultCellStyle.SelectionForeColor=Ink;
                grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(248,250,252);
                grid.RowTemplate.Height=42;
            }
            if(child is PictureBox)child.BackColor=Color.FromArgb(16,25,42);
            if(child is TabPage)child.Padding=new Padding(14);
        }
    }
}
