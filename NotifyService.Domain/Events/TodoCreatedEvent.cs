public record TodoCreatedEvent(
    Guid TodoId,
    string Title,
    string? Description); 