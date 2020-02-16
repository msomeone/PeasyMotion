using Microsoft.VisualStudio.Shell;

namespace PeasyMotion.Options
{
    /// <summary>
    /// A base class for a DialogPage to show in Tools -> Options.
    /// </summary>
    internal class BaseOptionPage<T> : DialogPage where T : BaseOptionModel<T>, new()
    {
        public BaseOptionModel<T> _model;
        private System.Windows.Forms.ComboBox _FontBox;

        public BaseOptionPage()
        {
#pragma warning disable VSTHRD104 // Offer async methods
            _model = ThreadHelper.JoinableTaskFactory.Run(BaseOptionModel<T>.CreateAsync);
#pragma warning restore VSTHRD104 // Offer async methods
            this._FontBox = new System.Windows.Forms.ComboBox();
            this._FontBox.Items.Add("Hehe");
            this._FontBox.Items.Add("Haha");
            var g = this.Window as System.Windows.Forms.PropertyGrid;
            var h = 2;
            h = h + 2;

        }

        public override object AutomationObject => _model;

        public override void LoadSettingsFromStorage()
        {
            _model.Load();
        }

        public override void SaveSettingsToStorage()
        {
            _model.Save();
        }
    }
}
