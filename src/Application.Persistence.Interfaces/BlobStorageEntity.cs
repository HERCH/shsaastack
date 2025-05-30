namespace Application.Persistence.Interfaces;

// En algún lugar de tu capa de persistencia (ej. Infrastructure.Persistence.Common.Entities)
public class BlobStorageEntity // O como prefieras llamarlo
{
    // Puedes usar un ID compuesto o un ID único global
    // Opción 1: ID Compuesto (si 'containerName' y 'blobName' son la clave)
    // [Key] // Si usas EF Core
    // public string ContainerName { get; set; }
    // [Key]
    // public string BlobName { get; set; }

    // Opción 2: ID Único (más simple para EF Core a veces)
    // [Key]
    // public Guid Id { get; set; } // O int, long
    public string Id { get; set; } // Podría ser containerName_blobName

    public string ContainerName { get; set; } // Para agrupar blobs, similar a contenedores de Azure
    public string BlobName { get; set; }      // El nombre/identificador del blob
    public string ContentType { get; set; }
    public byte[] Data { get; set; }          // El contenido del blob como bytes
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
    // Considera añadir metadatos adicionales si los necesitas (ej. tamaño, hash, etc.)
}