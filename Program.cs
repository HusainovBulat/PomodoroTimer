using System.Windows;

namespace PomodoroTimer;

static class Program
{
	[STAThread]
	static void Main() => new Application().Run(new MainWindow());
}