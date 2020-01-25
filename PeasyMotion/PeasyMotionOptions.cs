using System.ComponentModel;
using Microsoft.VisualStudio.Shell;
using PeasyMotion.Options;

namespace PeasyMotion
{

internal class GeneralOptions : BaseOptionModel<GeneralOptions>
{
    [Category("General")]
    [DisplayName("Jump label assignment algorithm")]
    [Description(
            "Affects jump label placement behaviour.\n" +
            "- CaretRelative - place shortest jump labels closer to caret,\n" +
            "label positions and values may be not reproducible if cursor moved slightly.\n" +
            "Sensivity to caret position can be adjusted using Caret position sensivity option.\n" +
            "Jump labels will be reproducible for caret positions differing \u00B1sensivity chars.\n" +
            "- ViewportRelative - assign labels starting from text viewport top, ignoring caret position."
            )]
    [DefaultValue(JumpLabelAssignmentAlgorithm.CaretRelative)]
    public JumpLabelAssignmentAlgorithm jumpLabelAssignmentAlgorithm { get; set; } = JumpLabelAssignmentAlgorithm.CaretRelative;

    [Category("General")]
    [DisplayName("Caret position sensivity")]
    [Description(
            "Sensivity to caret position during jump label assignment.\n" +
            "Jumplabels will be reproducible for caret positions differing \u00B1sensivity chars.\n"
            )]
    [DefaultValue(0)]
    public int caretPositionSensivity { get; set; } = 0;
}
internal class DialogPageProvider
{
    public class General : BaseOptionPage<GeneralOptions> { }
}

public enum JumpLabelAssignmentAlgorithm
{
    CaretRelative,
    ViewportRelative
}

}