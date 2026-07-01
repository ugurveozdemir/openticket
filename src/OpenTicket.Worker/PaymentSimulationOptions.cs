namespace OpenTicket.Worker;

public class PaymentSimulationOptions
{
    public const string SectionName = "PaymentSimulation";

    public double FailureRate { get; set; } = 0.2;
    public int MinDelaySeconds { get; set; } = 1;
    public int MaxDelaySeconds { get; set; } = 5;
}
