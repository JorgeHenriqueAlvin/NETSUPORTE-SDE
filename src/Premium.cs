namespace SdeSuporte;

static class Premium
{
    public static void Apply(Control root)
    {
        root.Font=new Font("Segoe UI",10);
        root.BackColor=Color.FromArgb(244,247,252);
        root.ForeColor=Color.FromArgb(28,43,65);
        foreach(Control child in root.Controls)
        {
            Apply(child);
            if(child is Button button)
            {
                button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;
                button.BackColor=Color.FromArgb(225,233,247);
                button.Padding=new Padding(9,5,9,5);button.Margin=new Padding(4);
                button.Cursor=Cursors.Hand;button.MinimumSize=new Size(0,34);
                button.FlatAppearance.MouseOverBackColor=Color.FromArgb(203,220,248);
            }
            if(child is TextBox or RichTextBox or ListBox)child.BackColor=Color.White;
            if(child is PictureBox)child.BackColor=Color.FromArgb(16,25,42);
            if(child is TabPage)child.Padding=new Padding(14);
        }
    }
}
