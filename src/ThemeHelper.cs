namespace TF2IdleManager;

public static class ThemeHelper
{
    public static void Apply(Form form, bool dark)
    {
        var canvas = dark ? Color.FromArgb(25, 31, 39) : SystemColors.Control;
        var surface = dark ? Color.FromArgb(35, 43, 54) : Color.White;
        var foreground = dark ? Color.FromArgb(232, 238, 245) : SystemColors.ControlText;
        void Style(Control item)
        {
            if (item is TextBoxBase or ComboBox or ListBox or NumericUpDown)
            {
                item.BackColor = surface;
                item.ForeColor = foreground;
            }
            else if (item is DataGridView dgv)
            {
                dgv.BackgroundColor = canvas;
                dgv.DefaultCellStyle.BackColor = surface;
                dgv.DefaultCellStyle.ForeColor = foreground;
                dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(62, 108, 161);
                dgv.DefaultCellStyle.SelectionForeColor = Color.White;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = surface;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = foreground;
                dgv.EnableHeadersVisualStyles = false;
                dgv.GridColor = dark ? Color.FromArgb(66, 77, 90) : Color.LightGray;
            }
            else if (item is Button button)
            {
                button.UseVisualStyleBackColor = false;
                button.BackColor = dark ? Color.FromArgb(52, 65, 82) : SystemColors.Control;
                button.ForeColor = foreground;
                button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
            }
            else
            {
                item.BackColor = canvas;
                item.ForeColor = foreground;
            }
            if (item is ThemedCheckBox checkbox) checkbox.DarkAppearance = dark;
            if (item is ThemedButton themedButton) themedButton.DarkAppearance = dark;
            foreach (Control child in item.Controls) Style(child);
        }
        Style(form);
    }
}
