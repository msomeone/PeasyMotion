using System;
using System.Collections.Generic;
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
        public JumpLabelUserControl()
        {
            InitializeComponent();
        }

        void init(string label, Rect bounds, double fontRenderingEmSize)
        {
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

        private static Stack<JumpLabelUserControl> cache = new Stack<JumpLabelUserControl>(1<<12);

        // warmup just a lil bit, dont allocate whole capacity, as it may slow down on startup.
        public static void WarmupCache()
        {
            for (int i = 0; i < 512; i++)
            {
                cache.Push(new JumpLabelUserControl());
            }
        }
        public static JumpLabelUserControl GetFreeUserControl(string label, Rect bounds, double fontRenderingEmSize)
        {
            JumpLabelUserControl ctrl = null;
            if (1 > cache.Count)
            {
                ctrl = new JumpLabelUserControl();
            } else {
                ctrl = cache.Pop();
            }
            ctrl.init(label, bounds, fontRenderingEmSize);
            return ctrl;
        }

        public static void ReleaseUserControl(JumpLabelUserControl ctrl) {
            cache.Push(ctrl);
        }
    }
}
