using System.Reflection;
using System.Windows.Forms;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    public static class CheckBoxExtension
    {
        public static void SetChecked(this CheckBox chBox, bool check)
        {
            typeof (CheckBox).GetField("checkState", BindingFlags.NonPublic |
                                                     BindingFlags.Instance)?.SetValue(chBox, check
                                                         ? CheckState.Checked
                                                         : CheckState.Unchecked);
            chBox.Invalidate();
        }
    }
}