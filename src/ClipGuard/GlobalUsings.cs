// UseWindowsForms + ImplicitUsings pulls System.Windows.Forms and System.Drawing
// into every file, which collides with WPF on a dozen common type names. WinForms
// is here only for NotifyIcon, so WPF wins every one of these globally.

global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Button = System.Windows.Controls.Button;
global using CheckBox = System.Windows.Controls.CheckBox;
global using Clipboard = System.Windows.Clipboard;
global using Color = System.Windows.Media.Color;
global using ContextMenu = System.Windows.Controls.ContextMenu;
global using Control = System.Windows.Controls.Control;
global using Cursors = System.Windows.Input.Cursors;
global using FontFamily = System.Windows.Media.FontFamily;
global using FontStyle = System.Windows.FontStyle;
global using Label = System.Windows.Controls.Label;
global using MenuItem = System.Windows.Controls.MenuItem;
global using MessageBox = System.Windows.MessageBox;
global using Orientation = System.Windows.Controls.Orientation;
global using Panel = System.Windows.Controls.Panel;
global using Point = System.Windows.Point;
global using RadioButton = System.Windows.Controls.RadioButton;
global using Screen = System.Windows.Forms.Screen;
global using Size = System.Windows.Size;
global using TextBox = System.Windows.Controls.TextBox;
global using UserControl = System.Windows.Controls.UserControl;
