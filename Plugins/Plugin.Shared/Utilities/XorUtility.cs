using Microsoft.Extensions.Logging;
using Shared.Correlation;

namespace Plugin.Shared.Utilities;

/// <summary>
/// Utility for XOR operations on byte data with configurable keys
/// Provides methods for encrypting/decrypting data using XOR cipher
/// </summary>
public static class XorUtility
{
    /// <summary>
    /// Performs XOR operation on data using a single byte key with hierarchical logging
    /// </summary>
    /// <param name="data">Data to XOR</param>
    /// <param name="key">Single byte XOR key</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="logger">Logger for hierarchical logging</param>
    /// <returns>XOR result as byte array</returns>
    public static byte[] XorWithByte(byte[] data, byte key, HierarchicalLoggingContext context, ILogger logger)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        logger.LogDebugWithHierarchy(context, "Performing XOR operation with single byte key on {DataLength} bytes", data.Length);

        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key);
        }

        logger.LogDebugWithHierarchy(context, "XOR operation completed successfully");
        return result;
    }

    /// <summary>
    /// Performs XOR operation on data using a byte array key with hierarchical logging
    /// </summary>
    /// <param name="data">Data to XOR</param>
    /// <param name="key">Byte array XOR key (will repeat if shorter than data)</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="logger">Logger for hierarchical logging</param>
    /// <returns>XOR result as byte array</returns>
    public static byte[] XorWithByteArray(byte[] data, byte[] key, HierarchicalLoggingContext context, ILogger logger)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (key == null || key.Length == 0)
        {
            throw new ArgumentException("XOR key cannot be null or empty", nameof(key));
        }

        logger.LogDebugWithHierarchy(context, "Performing XOR operation with {KeyLength}-byte key on {DataLength} bytes",
            key.Length, data.Length);

        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        }

        logger.LogDebugWithHierarchy(context, "XOR operation completed successfully");
        return result;
    }

    /// <summary>
    /// Performs XOR operation on data using a string key with hierarchical logging
    /// </summary>
    /// <param name="data">Data to XOR</param>
    /// <param name="key">String XOR key (will be converted to UTF-8 bytes)</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="logger">Logger for hierarchical logging</param>
    /// <returns>XOR result as byte array</returns>
    public static byte[] XorWithString(byte[] data, string key, HierarchicalLoggingContext context, ILogger logger)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("XOR key cannot be null or empty", nameof(key));
        }

        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        logger.LogDebugWithHierarchy(context, "Converting string key '{Key}' to {KeyLength} UTF-8 bytes", key, keyBytes.Length);

        return XorWithByteArray(data, keyBytes, context, logger);
    }

    /// <summary>
    /// Performs in-place XOR operation on data using a single byte key with hierarchical logging
    /// Modifies the original data array
    /// </summary>
    /// <param name="data">Data to XOR (modified in place)</param>
    /// <param name="key">Single byte XOR key</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="logger">Logger for hierarchical logging</param>
    public static void XorInPlace(byte[] data, byte key, HierarchicalLoggingContext context, ILogger logger)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        logger.LogDebugWithHierarchy(context, "Performing in-place XOR operation with single byte key on {DataLength} bytes", data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= key;
        }

        logger.LogDebugWithHierarchy(context, "In-place XOR operation completed successfully");
    }

    /// <summary>
    /// Performs in-place XOR operation on data using a byte array key with hierarchical logging
    /// Modifies the original data array
    /// </summary>
    /// <param name="data">Data to XOR (modified in place)</param>
    /// <param name="key">Byte array XOR key</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="logger">Logger for hierarchical logging</param>
    public static void XorInPlace(byte[] data, byte[] key, HierarchicalLoggingContext context, ILogger logger)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (key == null || key.Length == 0)
        {
            throw new ArgumentException("XOR key cannot be null or empty", nameof(key));
        }

        logger.LogDebugWithHierarchy(context, "Performing in-place XOR operation with {KeyLength}-byte key on {DataLength} bytes",
            key.Length, data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= key[i % key.Length];
        }

        logger.LogDebugWithHierarchy(context, "In-place XOR operation completed successfully");
    }

    /// <summary>
    /// Calculates a simple XOR checksum of the data with hierarchical logging
    /// </summary>
    /// <param name="data">Data to calculate checksum for</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="logger">Logger for hierarchical logging</param>
    /// <returns>XOR checksum as a single byte</returns>
    public static byte CalculateXorChecksum(byte[] data, HierarchicalLoggingContext context, ILogger logger)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        logger.LogDebugWithHierarchy(context, "Calculating XOR checksum for {DataLength} bytes", data.Length);

        byte checksum = 0;
        foreach (byte b in data)
        {
            checksum ^= b;
        }

        logger.LogDebugWithHierarchy(context, "XOR checksum calculated: 0x{Checksum:X2}", checksum);
        return checksum;
    }


}
