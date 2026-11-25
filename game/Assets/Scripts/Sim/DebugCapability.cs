namespace SpaceTrade.Sim
{
    /// <summary>
    /// Permission for debug commands (spec 13). Only <see cref="Grant"/> can make one, and it exists only in
    /// the Unity Editor and in Development Builds; in a normal build it is compiled out, so the permission
    /// cannot be granted. It belongs to the running game and is never stored in a save.
    /// </summary>
    public sealed class DebugCapability
    {
        private DebugCapability()
        {
        }

#if DEBUG
        // DEBUG is Unity's symbol for "the Editor or a Development Build" (Unity 6.6 deprecates DEVELOPMENT_BUILD).
        public static DebugCapability Grant() => new DebugCapability();
#endif
    }
}
