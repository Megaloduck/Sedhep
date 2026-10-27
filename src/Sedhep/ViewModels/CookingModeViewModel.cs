using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sedhep.Domain;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

/// <summary>
/// Presentation mode for following a recipe while cooking: one step at a time, large text, a
/// tick-off ingredient list, an optional timer for steps that carry one, and no editing anywhere.
/// </summary>
public partial class CookingModeViewModel : ViewModelBase
{
    private readonly ICookbookNavigator _navigator;
    private DispatcherTimer? _timer;
    private int _remainingSeconds;

    [ObservableProperty]
    private Recipe _recipe = null!;

    [ObservableProperty]
    private int _stepNumber = 1;

    [ObservableProperty]
    private bool _areIngredientsVisible = true;

    [ObservableProperty]
    private bool _isTimerRunning;

    [ObservableProperty]
    private string? _timerLabel;

    [ObservableProperty]
    private string? _timerButtonLabel = "Start timer";

    public ObservableCollection<CookIngredientViewModel> Ingredients { get; } = new();

    public ObservableCollection<RecipeStep> Steps { get; } = new();

    public CookingModeViewModel(ICookbookNavigator navigator, Recipe recipe)
    {
        _navigator = navigator;
        Recipe = recipe;

        foreach (var ingredient in recipe.Ingredients.OrderBy(i => i.SortOrder))
        {
            Ingredients.Add(new CookIngredientViewModel(ingredient));
        }

        foreach (var step in recipe.Steps.OrderBy(s => s.SortOrder))
        {
            Steps.Add(step);
        }
    }

    public RecipeStep? CurrentStep => Steps.Count == 0 ? null : Steps[StepNumber - 1];

    public string StepLabel => Steps.Count == 0
        ? "No steps yet"
        : $"Step {StepNumber} of {Steps.Count}";

    public bool HasSteps => Steps.Count > 0;

    public bool HasIngredients => Ingredients.Count > 0;

    public bool CanGoPrevious => StepNumber > 1;

    public bool CanGoNext => StepNumber < Steps.Count;

    public bool IsLastStep => Steps.Count > 0 && StepNumber == Steps.Count;

    /// <summary>0..1 for the progress bar at the top.</summary>
    public double Progress => Steps.Count <= 1 ? (Steps.Count == 1 ? 1 : 0) : (double)(StepNumber - 1) / (Steps.Count - 1);

    public double ProgressPercent => Progress * 100;

    public string NextLabel => IsLastStep ? "Finish" : "Next";

    public bool HasTimer => CurrentStep?.Minutes is > 0;

    public int IngredientCount => Ingredients.Count;

    public string IngredientCountLabel => Ingredients.Count == 1
        ? "1 ingredient"
        : $"{Ingredients.Count} ingredients";

    [RelayCommand]
    private void Previous()
    {
        if (CanGoPrevious)
        {
            StepNumber--;
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (CanGoNext)
        {
            StepNumber++;
        }
        else
        {
            _navigator.GoBack();
        }
    }

    [RelayCommand]
    private void ToggleIngredients() => AreIngredientsVisible = !AreIngredientsVisible;

    [RelayCommand]
    private void ToggleTimer()
    {
        if (IsTimerRunning)
        {
            StopTimer();
            return;
        }

        if (CurrentStep?.Minutes is not > 0)
        {
            return;
        }

        _remainingSeconds = CurrentStep.Minutes!.Value * 60;
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        IsTimerRunning = true;
        TimerButtonLabel = "Pause";
        UpdateTimerLabel();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            _remainingSeconds = 0;
            StopTimer();
            TimerLabel = "Time is up";
            return;
        }

        UpdateTimerLabel();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        IsTimerRunning = false;
        TimerButtonLabel = "Start timer";
    }

    private void UpdateTimerLabel()
    {
        var minutes = _remainingSeconds / 60;
        var seconds = _remainingSeconds % 60;
        TimerLabel = $"{minutes}:{seconds:00}";
    }

    [RelayCommand]
    private void Close() => _navigator.GoBack();

    partial void OnStepNumberChanged(int value)
    {
        StopTimer();
        TimerLabel = null;
        TimerButtonLabel = "Start timer";

        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(HasTimer));
    }
}
