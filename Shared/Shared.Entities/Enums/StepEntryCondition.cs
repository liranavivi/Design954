using System.ComponentModel;

namespace Shared.Entities.Enums;

/// <summary>
/// Defines the entry conditions that determine when a step should be executed.
/// These conditions control the workflow execution logic and step transitions.
/// Direct mapping to ActivityExecutionStatus values for precise control.
/// </summary>
public enum StepEntryCondition
{
    /// <summary>
    /// Execute only if the previous step is still processing.
    /// Useful for monitoring or parallel processing scenarios.
    /// </summary>
    [Description("Execute only if previous step is processing")]
    PreviousProcessing = 0,

    /// <summary>
    /// Execute only if the previous step completed successfully.
    /// This is the standard behavior for most workflow steps.
    /// </summary>
    [Description("Execute only if previous step completed")]
    PreviousCompleted = 1,

    /// <summary>
    /// Execute only if the previous step failed with an error.
    /// Useful for error handling, cleanup, or alternative processing paths.
    /// </summary>
    [Description("Execute only if previous step failed")]
    PreviousFailed = 2,

    /// <summary>
    /// Execute only if the previous step was cancelled.
    /// Useful for cleanup or cancellation handling workflows.
    /// </summary>
    [Description("Execute only if previous step was cancelled")]
    PreviousCancelled = 3,

    /// <summary>
    /// Always execute this step regardless of previous step results.
    /// Useful for initialization, logging, or mandatory processing steps.
    /// </summary>
    [Description("Always execute regardless of previous results")]
    Always = 4,

    /// <summary>
    /// Never execute this step - it is disabled.
    /// Useful for temporarily disabling steps without removing them.
    /// </summary>
    [Description("Never execute - step is disabled")]
    Never = 5,
}
