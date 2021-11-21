using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for WorkHoursPanelControl.xaml
    /// </summary>
    public partial class WorkHoursPanelControl : UserControl
    {
        /// <summary>Viewmodel of the Control Panel.</summary>
        ControlPanelViewModel _ViewModel { get { return DataContext as ControlPanelViewModel; } }

        public WorkHoursPanelControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Append signal values to the present work log.
        /// </summary>
        /// <param name="SignalValues"></param>
        /// <param name="Timestamp"></param>
        public void AppendSignalValues(LocalSignalDB.SignalValuesType SignalValues)
        {
            // FIXME: do what?
        }
    }
}
