using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace nottwrite.UI;

public partial class MainWindow
{
    // Two steps: a one-line identity, then an intent chooser that drops the user
    // straight into the right mode (instead of a generic feature tour).
    private const int IntentStep = 1;

    private readonly (string Icon, string Title, string Body)[] _onboardingSteps =
    {
        ("✍", "Welcome to nottwrite",
         "nottwrite turns your handwriting into a font — and lets you write notes and pages in it."),
        ("\U0001F44B", "What would you like to do?",
         "You can switch anytime from the sidebar. Pick where to start:"),
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
        ObTitle.Text = T(title);
        ObBody.Text  = T(body);

        bool intent = _onboardingStep == IntentStep;
        ObIntentPanel.Visibility = intent ? Visibility.Visible : Visibility.Collapsed;
        // On the intent step the choice itself advances — no Next button.
        ObNextBtn.Visibility = intent ? Visibility.Collapsed : Visibility.Visible;
        ObNextBtn.Content    = T(_onboardingStep == _onboardingSteps.Length - 1 ? "Get started" : "Next");
        ObSkipBtn.Visibility = Visibility.Visible;

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
            FinishOnboarding(null);
        }
    }

    private void Onboarding_PickIntent(object sender, RoutedEventArgs e)
    {
        var mode = (sender as FrameworkElement)?.Tag as string == "edit"
            ? AppMode.Edit : AppMode.Notes;
        FinishOnboarding(mode);
    }

    private void Onboarding_Skip(object sender, RoutedEventArgs e) => FinishOnboarding(null);

    private void FinishOnboarding(AppMode? jumpTo)
    {
        OnboardingOverlay.Visibility = Visibility.Collapsed;
        OnboardingSeen = true;
        SaveSettings();
        if (jumpTo is { } mode) SwitchMode(mode);
    }
}
