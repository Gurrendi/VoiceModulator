using System;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public enum ModulationMode
{
    Normal = 1,
    Robotic,
    Echo,
    Tremolo,
    WallE,
    DarthVader,
    Astartes,
    BattlefieldRadio,
    MilitaryNarrator,
}
