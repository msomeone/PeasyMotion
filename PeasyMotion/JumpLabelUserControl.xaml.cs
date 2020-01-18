using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PeasyMotion
{
    public partial class JumpLabelUserControl : UserControl
    {
        private const int BORDER = 2;
        private double oneCharWidth;

        private static string GetTrimmedLabel(string label) // trim max to two characters
        {
            _ = label ?? throw new ArgumentNullException(nameof(label), "cannot be null");
            return label.Substring(0, Math.Min(label.Length, 2));
            //return label; // debug: show full jump label
        }
        public JumpLabelUserControl(string label, Rect bounds, double fontRenderingEmSize)
        {
            InitializeComponent();

            this.oneCharWidth = bounds.Width;

            var str = GetTrimmedLabel(label); 
            this.Content = str;
            this.Background = Brushes.GreenYellow;
            this.Width = this.oneCharWidth * str.Length + BORDER;
            this.Height = bounds.Height;
   
            this.FontWeight = FontWeights.Bold;

            this.FontSize = fontRenderingEmSize;
        }

        public void UpdateView(string alreadyPressedKeys)
        {
            _ = alreadyPressedKeys ?? throw new ArgumentNullException(nameof(alreadyPressedKeys), "cannot be null");

            var str = GetTrimmedLabel(alreadyPressedKeys);
            this.Width = this.oneCharWidth * str.Length + BORDER;
            this.Content = str;
            if (str.Length == 1) {
                this.Background = Brushes.LightGray;
                this.Foreground = Brushes.Red;
            }
        }
    }
}
