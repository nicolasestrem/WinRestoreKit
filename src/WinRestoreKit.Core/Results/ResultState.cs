namespace WinRestoreKit
{
    /// <summary>
    /// The outcome of one sub-operation or one module.
    /// </summary>
    /// <remarks>
    /// Three states answer three different user questions: did I get my data (Succeeded), was there
    /// nothing to get (Skipped), did something break (Failed). There is deliberately no Partial:
    /// it carries nothing that Succeeded/Failed plus the step list does not already carry, and it
    /// forces every consumer to answer a question with no stable answer - is Partial good news?
    /// </remarks>
    public enum ResultState
    {
        Succeeded,
        Skipped,
        Failed
    }
}
