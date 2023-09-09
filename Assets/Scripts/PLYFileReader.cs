using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;


public static class PLYFileReader
{
    public static void ReadFile(string filePath, out int vertexCount, out int vertexBytes, out List<string> fieldNames, out NativeArray<byte> vertices)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        // fix: create buffers larger than 2GB...
        if (fs.Length >= 2 * 1024 * 1024 * 1024L)
        {
            throw new IOException("Read error: PLY files larger than 2GB are not supported");
        }

        vertexBytes = 0;
        vertexCount = 0;
        fieldNames = new List<string>();

        // Read header
        while (true)
        {
            var line = ReadLine(fs);
            if (line == "end_header")
                break;

            // splatCount
            var tokens = line.Split(' ');
            if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
            {
                vertexCount = int.Parse(tokens[2]);
            }

            // Field names (x/y/z, nx/ny/nz, size_x/y/z, opacity, etc.)
            if (tokens.Length == 3 && tokens[0] == "property")
            {
                if (tokens[1] == "float")
                {
                    vertexBytes += 4; // 32bit
                }
                else if (tokens[1] == "double")
                {
                    vertexBytes += 8; // 64bit
                }
                else if (tokens[1] == "uchar")
                {
                    vertexBytes += 1; // 8bit
                }
                fieldNames.Add(tokens[2]);
            }
        }

        // Copy data to vertices buffer
        vertices = new NativeArray<byte>(vertexCount * vertexBytes, Allocator.Persistent);
        var readBytes = fs.Read(vertices);

        if (readBytes != vertices.Length)
        {
            throw new IOException($"Read error, expected {vertices.Length} data bytes got {readBytes}");
        }
    }

    static string ReadLine(FileStream fs)
    {
        var byteBuffer = new List<byte>();
        while (true)
        {
            int byte_ = fs.ReadByte();
            if (byte_ == -1 || byte_ == '\n')
                break;
            byteBuffer.Add((byte)byte_);
        }
        return Encoding.UTF8.GetString(byteBuffer.ToArray());
    }
}