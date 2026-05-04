using System;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentTaskScheduler
{
    public sealed partial class QuickActionsPage : Page
    {
        public QuickActionsViewModel ViewModel { get; } = new QuickActionsViewModel();

        public QuickActionsPage()
        {
            this.InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Title is set via x:Uid in XAML
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QuickActionItemViewModel action)
            {
                await ViewModel.ExecuteAction(action);
            }
        }
    }
}
