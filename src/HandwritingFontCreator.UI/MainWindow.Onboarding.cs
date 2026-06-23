using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HandwritingFontCreator.UI;

public partial class MainWindow
{
    private readonly (string Icon, string Title, string Body)[] _onboardingSteps =
    {
        ("✍", "Welcome to nottwrite",
         "Turn your own handwriting into a real font you can install and type with anywhere — Word, the browser, anywhere."),
        ("✏", "1 · Draw your letters",
         "Open Edit mode and draw each character once on the grid. A pen or stylus gives the most natural result. The live preview shows your font as you go."),
        ("↓", "2 · Export your font",
         "When enough letters are drawn, hit Export .ttf in the Template panel. Install the file and your handwriting works system-wide."),
        ("\U0001F4D3", "3 · Write notes in your hand",
         "Use Type and Notes to write whole pages in your font, organise them in folders, and export to PNG, PDF or SVG."),
    };

    private int _onboardingStep;

    private void MaybeShowOnboarding()
    {
        if (OnboardingSeen) return;
        _onboardingStep = 0;
        RenderOnboardingStep();
        OnboardingOverlay.Visibility = Visibility.Visible;
    }

    private void RenderOnboardingStep()
    {
        var (icon, title, body) = _onboardingSteps[_onboardingStep];
        ObIcon.Text  = icon;
        ObTitle.Text = title;
        ObBody.Text  = body;
        ObNextBtn.Content = _onboardingStep == _onboardingSteps.Length - 1 ? "Get started" : "Next";
        ObSkipBtn.Visibility = _onboardingStep == _onboardingSteps.Length - 1
            ? Visibility.Hidden : Visibility.Visible;

        ObDots.Children.Clear();
        for (int i = 0; i < _onboardingSteps.Length; i++)
        {
            bool active = i == _onboardingStep;
            ObDots.Children.Add(new Border
            {
                Width = active ? 20 : 7, Height = 7,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(3, 0, 3, 0),
                Background = active ? GetBrush("AccentBrush") : GetBrush("AppBorderBrush"),
            });
        }
    }

    private void Onboarding_Next(object sender, RoutedEventArgs e)
    {
        if (_onboardingStep < _onboardingSteps.Length - 1)
        {
            _onboardingStep++;
            RenderOnboardingStep();
        }
        else
        {
            FinishOnboarding(jumpToEdit: true);
        }
    }

    private void Onboarding_Skip(object sender, RoutedEventArgs e) => FinishOnboarding(jumpToEdit: false);

    private void FinishOnboarding(bool jumpToEdit)
    {
        OnboardingOverlay.Visibility = Visibility.Collapsed;
        OnboardingSeen = true;
        SaveSettings();
        if (jumpToEdit) SwitchMode(AppMode.Edit);
    }
}
