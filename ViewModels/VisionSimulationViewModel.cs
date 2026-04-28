using System;
using System.Windows;
using Colorblind_Asisstant.Model;

namespace Colorblind_Asisstant.ViewModels
{
    public class VisionSimulationViewModel : INotifyPropertyChanged
    {
        private string _currentMode = "Normal";
        private string _modeLabel = "Vision Simulation: OFF";
        private string _instructionsText = "👀 Hover over any color in your browser or app to test how Protanopia changes the colors you see!";
        private string _protanopiaInfoText = "A form of colorblindness where red cone cells (L-cones) don't function normally. Red and green colors appear more similar, making it difficult to distinguish red-green combinations.";
        private string _protanopiaUsersText = "Affects approximately 8% of men and 0.5% of women (X-linked trait). Also called 'red-blindness'.";
        private string _helperInfoText = "This simulation applies an LMS transformation matrix to approximate how red-blindness affects color perception. Great for accessibility testing and inclusive design!";

        public string CurrentMode
        {
            get => _currentMode;
            set
            {
                _currentMode = value;
                UpdateModeDisplay();
            }
        }

        public string ModeLabel
        {
            get => _modeLabel;
            private set => SetProperty(ref _modeLabel, value);
        }

        public string InstructionsText
        {
            get => _instructionsText;
            private set => SetProperty(ref _instructionsText, value);
        }

        public string ProtanopiaInfoText
        {
            get => _protanopiaInfoText;
            private set => SetProperty(ref _protanopiaInfoText, value);
        }

        public string ProtanopiaUsersText
        {
            get => _protanopiaUsersText;
            private set => SetProperty(ref _protanopiaUsersText, value);
        }

        public string HelperInfoText
        {
            get => _helperInfoText;
            private set => SetProperty(ref _helperInfoText, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isLmsMatrixApplied = false;

        public bool IsLmsMatrixApplied
        {
            get => _isLmsMatrixApplied;
            set
            {
                _isLmsMatrixApplied = value;
                UpdateModeDisplay();
            }
        }

        private void UpdateModeDisplay()
        {
            ModeLabel = CurrentMode == "Normal" 
                ? "Vision Simulation: OFF" 
                : "Vision Simulation: ON (Protanopia)";
            
            InstructionsText = CurrentMode == "Normal" 
                ? "👀 Hover over any color to see normal colors"
                : "👁️ Simulating Protanopia - hover to see red-blindness simulation!";
        }

        /// <summary>
        /// Applies the LMS transformation matrix for Protanopia (red-blindness)
        /// Uses the LMS cone space with appropriate transformation matrix
        /// </summary>
        public Color? TransformColorForProtanopia(Color originalColor, double intensity = 1.0)
        {
            if (!IsLmsMatrixApplied || currentMode == "Normal")
                return null;

            // Convert RGB to LMS color space
            // Then apply the protanopia transformation matrix
            
            // Normal RGB to LMS transformation
            double[] rgb = new double[3];
            rgb[0] = originalColor.R / 255.0 * 100.0;
            rgb[1] = originalColor.G / 255.0 * 100.0;
            rgb[2] = originalColor.B / 255.0 * 100.0;

            // LMS transformation matrix (from RGB to LMS)
            double[][] rgbToLmsMatrix = {
                new double[] { 0.64, 0.36, -0.34 },
                new double[] { -0.27, 0.56, 0.18 },
                new double[] { -0.29, 0.25, 0.53 }
            };

            // Apply RGB to LMS transformation
            double[] lms = new double[3];
            for (int i = 0; i < 3; i++)
            {
                lms[i] = 0;
                for (int j = 0; j < 3; j++)
                {
                    lms[i] += rgbToLmsMatrix[i][j] * rgb[j];
                }
            }

            // Protanopia transformation matrix (L-cone dysfunction)
            // This simulates red-blindness by adjusting L cone response
            double[][] protanopiaMatrix = {
                new double[] { 0.13, -0.48, 0.56 },  // L' cone with reduced sensitivity
                new double[] {-0.27, 0.56, 0.18 },  // M' cone (maintained)
                new double[] {-0.29, 0.25, 0.53 }   // S' cone (unchanged)
            };

            // Apply protanopia transformation to LMS
            double[] protanopiaLms = new double[3];
            for (int i = 0; i < 3; i++)
            {
                protanopiaLms[i] = 0;
                for (int j = 0; j < 3; j++)
                {
                    protanopiaLms[i] += protanopiaMatrix[i][j] * lms[j];
                }
            }

            // Convert back from LMS to RGB
            double[][] lmsToRgbMatrix = {
                new double[] { -0.96, 0.42, 1.5 },
                new double[] { 0.38, 0.17, -0.3 },
                new double[] {-0.27, 0.24, -0.14 }
            };

            double[] resultRgb = new double[3];
            for (int i = 0; i < 3; i++)
            {
                resultRgb[i] = 0;
                for (int j = 0; j < 3; j++)
                {
                    resultRgb[i] += lmsToRgbMatrix[i][j] * protanopiaLms[j];
                }
            }

            // Convert back to RGB scale (0-255)
            double finalR = Math.Clamp(resultRgb[0] * 255.0 / 100.0, 0, 255);
            double finalG = Math.Clamp(resultRgb[1] * 255.0 / 100.0, 0, 255);
            double finalB = Math.Clamp(resultRgb[2] * 255.0 / 100.0, 0, 255);

            return Color.FromRgb((byte)finalR, (byte)finalG, (byte)finalB);
        }

        private string currentMode { get => _currentMode; set { _currentMode = value; UpdateModeDisplay(); OnPropertyChanged(nameof(CurrentMode)); } }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T backingStore, T value, string? name = null)
        {
            if (backingStore == value) return false;
            backingStore = value;
            OnPropertyChanged(name);
            return true;
        }

        // Event for going back to color reading page
        public event EventHandler? NavigateBackRequested;
    }
}