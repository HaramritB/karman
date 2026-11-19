namespace Karman.Simulation
{
    public enum Scenario { OrbitSandbox, LunarStackLeo, LunarOrbitLander }

    /// <summary>Which scenario the Flight scene starts (set by the menu; a VAB launch request takes precedence).</summary>
    public static class GameStart
    {
        public static Scenario Scenario = Scenario.OrbitSandbox;
        public static Scenario FromCommandLine()
        {
            string s = CommandLine.Value("-karmanScenario");
            if (s == "lunar-leo") return Scenario.LunarStackLeo;
            if (s == "lunar-orbit") return Scenario.LunarOrbitLander;
            return Scenario;
        }
    }
}
