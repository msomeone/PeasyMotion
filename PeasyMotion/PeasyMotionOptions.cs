using System.Diagnostics;
using System.ComponentModel;
using Microsoft.VisualStudio.Shell;
using PeasyMotion.Options;
using System.Drawing;
using System.Collections.Generic;
using Microsoft.VisualStudio.ComponentModelHost;
using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.TextManager.Interop;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections;
using System.Windows.Media;

namespace PeasyMotion
{
#region ColorKey 

readonly struct ColorKey
{
    internal readonly string Name;
    internal readonly bool IsForeground;

    internal ColorKey(string name, bool isForeground)
    {
        Name = name;
        IsForeground = isForeground;
    }

    internal static ColorKey Foreground(string name)
    {
        return new ColorKey(name, isForeground: true);
    }

    internal static ColorKey Background(string name)
    {
        return new ColorKey(name, isForeground: false);
    }
}

#endregion

#region ColorInfo

sealed class ColorInfo
{
    internal readonly ColorKey ColorKey;
    internal readonly System.Drawing.Color OriginalColor;
    internal bool IsValid;
    internal System.Drawing.Color Color;

    internal ColorInfo(ColorKey colorKey, System.Drawing.Color color, bool isValid = true)
    {
        ColorKey = colorKey;
        OriginalColor = color;
        Color = color;
        IsValid = isValid;
    }
}

#endregion

internal class GeneralOptions : BaseOptionModel<GeneralOptions>
{
    private static readonly ColorKey s_jumplabelBG = ColorKey.Background(PeasyMotionJumplabelFormatDef.FMT_NAME);
    private static readonly ColorKey s_jumplabelFG = ColorKey.Foreground(PeasyMotionJumplabelFormatDef.FMT_NAME);

    private static readonly ReadOnlyCollection<ColorKey> s_colorKeyList = new ReadOnlyCollection<ColorKey>(new[]
    {
        s_jumplabelBG,
        s_jumplabelFG,
    });

    private readonly Dictionary<ColorKey, ColorInfo> colorMap = new Dictionary<ColorKey, ColorInfo>();

    public GeneralOptions()
    {
        foreach (var colorKey in s_colorKeyList)
        {
            colorMap[colorKey] = new ColorInfo(colorKey, System.Drawing.Color.Black);
        }
    }

    [Category("General")]
    [DisplayName("Jump label background color")]
    [DisableOptionSerialization]
    public System.Drawing.Color JumpLabelBackgroundColor
    {
        get { return GetColor(s_jumplabelBG); }
        set { 
            Trace.WriteLine($"GeneralOptions.JumpLabelBackgroundColor property set color={value}");
            if (JumpLabelBackgroundColor != value) { // avoid infinite notification loop between VsSettings and GeneralOptions
                Trace.WriteLine($"GeneralOptions.JumpLabelBackgroundColor property differs by val. Calling GeneralOptions.SetColor");
            }
            SetColor(nameof(JumpLabelBackgroundColor), s_jumplabelBG, value);
        }
    }

    [Category("General")]
    [DisplayName("Jump label foreground color")]
    [DisableOptionSerialization]
    public System.Drawing.Color JumpLabelForegroundColor
    {
        get { return GetColor(s_jumplabelFG); }
        set { 
            Trace.WriteLine($"GeneralOptions.JumpLabelForegroundColor property set color={value}");
            if (JumpLabelForegroundColor != value) { // avoid infinite notification loop between VsSettings and GeneralOptions
                Trace.WriteLine($"GeneralOptions.JumpLabelForegroundColor property differs by val. Calling GeneralOptions.SetColor");
            }
            SetColor(nameof(JumpLabelForegroundColor), s_jumplabelFG, value);
        }
    }

    public static System.Windows.Media.Color fromDrawingColor(System.Drawing.Color c) {
        return System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);
    }
    public static System.Drawing.Color toDrawingColor(System.Windows.Media.Color c) {
        return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    public System.Windows.Media.Color getJumpLabelBackgroundColorMediaColor() { return fromDrawingColor(JumpLabelBackgroundColor); }
    public System.Windows.Media.Color getJumpLabelForegroundColorMediaColor() { return fromDrawingColor(JumpLabelForegroundColor); }

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
    /*
    [Category("General")]
    [DisplayName("ListTEST")]
    [Description(
            "!!!!!!!!!!!!!!!!\n"
            )]
    public System.Drawing.Color labelColor{ get; set; } = System.Drawing.Color.FromArgb(255,56,67,33);

    [Category("General")]
    [DisplayName("ListTEST2222")]
    [Description( "!!!!!!!!!!!!!!!!\n" )]
    public List<System.Drawing.Color> labelColor333{ get; set; } = new List<System.Drawing.Color>(){
            System.Drawing.Color.FromArgb(255,56,67,33),
            System.Drawing.Color.FromArgb(255,0,67,33),
            System.Drawing.Color.FromArgb(255,56,0,33),
            System.Drawing.Color.FromArgb(255,56,67,0)
    };
*/

    private System.Drawing.Color GetColor(ColorKey colorKey) { 
        Trace.WriteLine($"GeneralOptions.GetColor keyName={colorKey.Name}" + (colorKey.IsForeground? "FG":"BG") + $" color={colorMap[colorKey].Color}");
        return colorMap[colorKey].Color; 
    }

    private void SetColor(string propertyName, ColorKey colorKey, System.Drawing.Color value) { 
        Trace.WriteLine($"GeneralOptions.SetColor keyName={colorKey.Name}" + (colorKey.IsForeground? "FG":"BG") + $" color={value}");
        colorMap[colorKey].Color = value; 
        VsSettings.NotiifyInstancesFmtPropertyChanged(propertyName, fromDrawingColor(value));
    }
        

    public void LoadColors(IServiceProvider Site)
    {
        Trace.WriteLine($"GeneralOptions.LoadColors");
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var guid = Microsoft.VisualStudio.Editor.DefGuidList.guidTextEditorFontCategory;
            var flags = __FCSTORAGEFLAGS.FCSF_LOADDEFAULTS;//| __FCSTORAGEFLAGS.FCSF_NOAUTOCOLORS;
            var vsStorage = (IVsFontAndColorStorage)(Site.GetService(typeof(SVsFontAndColorStorage)));
            ErrorHandler.ThrowOnFailure(vsStorage.OpenCategory(ref guid, (uint)flags));
            LoadColorsCore(Site, vsStorage);
            ErrorHandler.ThrowOnFailure(vsStorage.CloseCategory());
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"PeasyMotion exception in Options.LoadColors: {ex.ToString()}");
        }
    }

    private void LoadColorsCore(IServiceProvider Site, IVsFontAndColorStorage vsStorage)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (var colorKey in s_colorKeyList)
        {
            ColorInfo colorInfo;
            try
            {
                var color = LoadColor(Site, vsStorage, colorKey);
                colorInfo = new ColorInfo(colorKey, color);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"PeasyMotion exception in Options.LoadColorsCore: {ex.ToString()}");
                colorInfo = new ColorInfo(colorKey, System.Drawing.Color.Black, isValid: false);
            }

            colorMap[colorKey] = colorInfo;
        }
    }

        public void SaveColors(IServiceProvider Site)
        {
            Trace.WriteLine($"GeneralOptions.SaveColors");
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Site == null)
            {
                return;
            }

            try
            {
                var guid = Microsoft.VisualStudio.Editor.DefGuidList.guidTextEditorFontCategory;
                var flags = __FCSTORAGEFLAGS.FCSF_LOADDEFAULTS | __FCSTORAGEFLAGS.FCSF_PROPAGATECHANGES;
                var vsStorage = (IVsFontAndColorStorage)(Site.GetService(typeof(SVsFontAndColorStorage)));
                var vsStorageCache = (IVsFontAndColorCacheManager )(Site.GetService(typeof(SVsFontAndColorCacheManager)));

                ErrorHandler.ThrowOnFailure(vsStorage.OpenCategory(ref guid, (uint)flags));
                foreach (var colorInfo in colorMap.Values)
                {
                    SaveColor(vsStorage, colorInfo.ColorKey, colorInfo.Color);
                }
                ErrorHandler.ThrowOnFailure(vsStorage.CloseCategory());

                if (vsStorageCache != null) {
                    vsStorageCache.ClearAllCaches();
                    vsStorageCache.RefreshCache(Microsoft.VisualStudio.Editor.DefGuidList.guidTextEditorFontCategory);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"PeasyMotion exception in Options.SaveColors(): {ex.ToString()}");
            }
        }

    private static System.Drawing.Color LoadColor(IServiceProvider Site, IVsFontAndColorStorage vsStorage, ColorKey colorKey)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var arr = new ColorableItemInfo[1];
        ErrorHandler.ThrowOnFailure(vsStorage.GetItem(colorKey.Name, arr));

        var isValid = colorKey.IsForeground ? arr[0].bForegroundValid : arr[0].bBackgroundValid;
        if (isValid == 0)
        {
            throw new Exception();
        }

        var colorRef = colorKey.IsForeground ? arr[0].crForeground : arr[0].crBackground;
        var color = FromColorRef(Site, vsStorage, colorRef);

        Trace.WriteLine($"GeneralOptions.LoadColoR keyName={colorKey.Name} color={color}");
        return color;
    }

    private static void SaveColor(IVsFontAndColorStorage vsStorage, ColorKey colorKey, System.Drawing.Color color)
    {
        Trace.WriteLine($"GeneralOptions.SaveColoR keyName={colorKey.Name} color={color}");
        ThreadHelper.ThrowIfNotOnUIThread();
        ColorableItemInfo[] arr = new ColorableItemInfo[1];
        ErrorHandler.ThrowOnFailure(vsStorage.GetItem(colorKey.Name, arr));
        if (colorKey.IsForeground)
        {
            arr[0].bForegroundValid = 1;
            arr[0].crForeground = (uint)ColorTranslator.ToWin32(color);
        }
        else
        {
            arr[0].bBackgroundValid = 1;
            arr[0].crBackground = (uint)ColorTranslator.ToWin32(color);
        }
        ErrorHandler.ThrowOnFailure(vsStorage.SetItem(colorKey.Name, arr));
    }

    private static System.Drawing.Color FromColorRef(IServiceProvider Site, IVsFontAndColorStorage vsStorage, uint colorValue)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var vsUtil = (IVsFontAndColorUtilities)vsStorage;
        ErrorHandler.ThrowOnFailure(vsUtil.GetColorType(colorValue, out int type));
        switch ((__VSCOLORTYPE)type)
        {
            case __VSCOLORTYPE.CT_SYSCOLOR:
            case __VSCOLORTYPE.CT_RAW:
                return ColorTranslator.FromWin32((int)colorValue);
            case __VSCOLORTYPE.CT_COLORINDEX:
                {
                    var array = new COLORINDEX[1];
                    ErrorHandler.ThrowOnFailure(vsUtil.GetEncodedIndex(colorValue, array));
                    ErrorHandler.ThrowOnFailure(vsUtil.GetRGBOfIndex(array[0], out uint rgb));
                    return ColorTranslator.FromWin32((int)rgb);
                };
            case __VSCOLORTYPE.CT_VSCOLOR:
                {
                    var vsUIShell = (IVsUIShell2)Site.GetService(typeof(SVsUIShell));
                    ErrorHandler.ThrowOnFailure(vsUtil.GetEncodedVSColor(colorValue, out int index));
                    ErrorHandler.ThrowOnFailure(vsUIShell.GetVSSysColorEx(index, out uint rgbValue));
                    return ColorTranslator.FromWin32((int)rgbValue);
                };
            case __VSCOLORTYPE.CT_AUTOMATIC:
            case __VSCOLORTYPE.CT_TRACK_BACKGROUND:
            case __VSCOLORTYPE.CT_TRACK_FOREGROUND:
            case __VSCOLORTYPE.CT_INVALID:
                return System.Drawing.Color.Transparent;
            default:
                return System.Drawing.Color.Black;
        }
    }
}

internal class DialogPageProvider
{
    public class General : BaseOptionPage<GeneralOptions> 
    { 
        protected override void OnActivate(CancelEventArgs e)
        {
            Trace.WriteLine($"GeneralOptions OnActivate");
            ThreadHelper.ThrowIfNotOnUIThread();
            base.OnActivate(e);
            (base._model as GeneralOptions).LoadColors(this.Site);
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            Trace.WriteLine($"GeneralOptions OnApply");
            ThreadHelper.ThrowIfNotOnUIThread();
            base.OnApply(e);
            (base._model as GeneralOptions).SaveColors(this.Site);
        }
    }
}

public enum JumpLabelAssignmentAlgorithm
{
    CaretRelative,
    ViewportRelative
}

}