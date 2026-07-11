using ModbusForge.Models;
using System.Windows;

namespace ModbusForge.Views
{
    /// <summary>
    /// Dialog that shows a <see cref="GroupDeletionPreview"/> and lets the user choose
    /// how to handle the group's contents before deletion.
    /// </summary>
    public partial class GroupDeletionDialog : Window
    {
        /// <summary>The mode chosen by the user. Valid only when <see cref="DialogResult"/> is true.</summary>
        public GroupDeletionMode ChosenMode { get; private set; } = GroupDeletionMode.MoveToParent;

        public GroupDeletionDialog(GroupDeletionPreview preview)
        {
            InitializeComponent();
            ApplyPreview(preview);
            WireRadioButtons();
        }

        private void ApplyPreview(GroupDeletionPreview preview)
        {
            HeaderText.Text = $"Delete group \"{preview.GroupName}\"";

            DirectTagCountText.Text       = preview.DirectTagCount.ToString();
            RecursiveTagCountText.Text    = preview.RecursiveTagCount.ToString();
            RecursiveSubgroupCountText.Text = preview.RecursiveSubgroupCount.ToString();
            WatchEntryCountText.Text      = preview.WatchEntriesToRemove.ToString();

            // Label the MoveToParent option with the actual destination name
            var parentLabel = string.IsNullOrEmpty(preview.DestinationGroupName)
                ? "Default"
                : preview.DestinationGroupName;

            MoveToParentLabel.Text =
                $"Move contents to parent group: \"{parentLabel}\"";

            MoveToDefaultLabel.Text = "Move contents to Default group";

            // If the destination IS already Default (no parent), disable the MoveToParent option
            // and pre-select MoveToDefault instead, but keep both visible.
            if (preview.DestinationGroupName == "Default"
                && string.IsNullOrEmpty(preview.DestinationGroupId))
            {
                // Still valid – destination is Default
            }
        }

        private void WireRadioButtons()
        {
            MoveToParentRadio.Checked   += (_, _) => UpdateDeleteButton();
            MoveToDefaultRadio.Checked  += (_, _) => UpdateDeleteButton();
            CascadeDeleteRadio.Checked  += (_, _) => OnCascadeSelected();
            CascadeDeleteRadio.Unchecked += (_, _) => OnCascadeDeselected();
        }

        private void OnCascadeSelected()
        {
            ConfirmCascadeCheck.Visibility = Visibility.Visible;
            ConfirmCascadeCheck.IsChecked  = false;
            DeleteButton.IsEnabled = false;
        }

        private void OnCascadeDeselected()
        {
            ConfirmCascadeCheck.Visibility = Visibility.Collapsed;
            ConfirmCascadeCheck.IsChecked  = false;
            UpdateDeleteButton();
        }

        private void ConfirmCascadeCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdateDeleteButton();
        }

        private void UpdateDeleteButton()
        {
            if (CascadeDeleteRadio.IsChecked == true)
            {
                DeleteButton.IsEnabled = ConfirmCascadeCheck.IsChecked == true;
            }
            else
            {
                DeleteButton.IsEnabled = true;
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (MoveToParentRadio.IsChecked == true)
                ChosenMode = GroupDeletionMode.MoveToParent;
            else if (MoveToDefaultRadio.IsChecked == true)
                ChosenMode = GroupDeletionMode.MoveToDefault;
            else
                ChosenMode = GroupDeletionMode.CascadeDelete;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
