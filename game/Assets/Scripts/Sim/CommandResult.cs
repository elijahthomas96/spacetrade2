namespace SpaceTrade.Sim
{
    /// <summary>
    /// Outcome of a player or automation command. A failed command changes nothing
    /// and carries a reason the interface can show.
    /// </summary>
    public readonly struct CommandResult
    {
        public bool Succeeded { get; }
        public string Reason { get; }

        private CommandResult(bool succeeded, string reason)
        {
            Succeeded = succeeded;
            Reason = reason;
        }

        public static CommandResult Ok() => new CommandResult(true, null);

        public static CommandResult Fail(string reason) => new CommandResult(false, reason);

        public override string ToString() => Succeeded ? "Ok" : "Failed: " + Reason;
    }
}
