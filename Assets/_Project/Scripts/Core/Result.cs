namespace ARO.Core
{
    /// <summary>Success-or-error return that carries a player-facing message.</summary>
    public readonly struct Result<T>
    {
        public readonly bool Ok;
        public readonly T Value;
        public readonly string ErrorCode;     // machine code, e.g. "delivery_too_fast"
        public readonly string UserMessage;   // safe to show in UI

        Result(bool ok, T v, string code, string msg) { Ok = ok; Value = v; ErrorCode = code; UserMessage = msg; }
        public static Result<T> Success(T v) => new Result<T>(true, v, null, null);
        public static Result<T> Fail(string code, string msg) => new Result<T>(false, default, code, msg);
    }
}
