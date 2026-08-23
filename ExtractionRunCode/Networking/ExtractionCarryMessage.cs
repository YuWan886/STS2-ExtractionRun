using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using ExtractionRun.Data;

namespace ExtractionRun.Networking;

public struct ExtractionCarryMessage : INetMessage, IPacketSerializable
{
    private const int MaxPayloadChars = 32768;

    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };

    public string Payload;

    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public static ExtractionCarryMessage From(CarryConfig config)
    {
        return new ExtractionCarryMessage
        {
            Payload = JsonSerializer.Serialize(config, JsonOptions),
        };
    }

    public CarryConfig Decode()
    {
        if (string.IsNullOrWhiteSpace(Payload) || Payload.Length > MaxPayloadChars)
        {
            throw new JsonException("Invalid extraction carry payload length.");
        }

        return JsonSerializer.Deserialize<CarryConfig>(Payload, JsonOptions) ?? new CarryConfig();
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(Payload ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        Payload = reader.ReadString();
    }
}
