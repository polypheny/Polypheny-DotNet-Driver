/*
 * Copyright 2019-2026 The Polypheny Project
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver;

/// <summary>A node or edge in a graph result.</summary>
public abstract class GraphElement
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<string> Labels { get; }
    public IReadOnlyDictionary<string, object> Properties { get; }

    private protected GraphElement(string id, string name, IEnumerable<string> labels, IDictionary<string, ProtoValue> properties)
    {
        Id = id;
        Name = name;
        Labels = Array.AsReadOnly(labels.ToArray());
        Properties = new ReadOnlyDictionary<string, object>(
            properties.ToDictionary(property => property.Key, property => ProtoHelper.FromProtoValue(property.Value)));
    }
}

/// <summary>A graph node, including its server identifier, labels and properties.</summary>
public sealed class GraphNode : GraphElement
{
    internal GraphNode(ProtoNode node) : base(node.Id, node.Name, node.Labels, node.Properties) { }
}

/// <summary>The orientation of an edge relative to its left and right endpoints.</summary>
public enum GraphDirection
{
    Unspecified = 0,
    LeftToRight = 1,
    RightToLeft = 2,
    None = 3
}

/// <summary>A graph edge. Endpoint orientation is retained when a path traverses it in reverse.</summary>
public sealed class GraphEdge : GraphElement
{
    public string Left { get; }
    public string Right { get; }
    public GraphDirection Direction { get; }

    internal GraphEdge(ProtoEdge edge) : base(edge.Id, edge.Name, edge.Labels, edge.Properties)
    {
        Left = edge.Left;
        Right = edge.Right;
        Direction = (GraphDirection)edge.Direction;
    }
}

/// <summary>An ordered path. Nodes and edges follow traversal order.</summary>
public sealed class GraphPath
{
    public IReadOnlyList<GraphNode> Nodes { get; }
    public IReadOnlyList<GraphEdge> Edges { get; }

    internal GraphPath(ProtoPath path)
    {
        Nodes = Array.AsReadOnly(path.Nodes.Select(node => new GraphNode(node)).ToArray());
        Edges = Array.AsReadOnly(path.Edges.Select(edge => new GraphEdge(edge)).ToArray());
    }
}

/// <summary>A whole graph, including isolated nodes. Node and edge order is unspecified.</summary>
public sealed class GraphValue
{
    public IReadOnlyList<GraphNode> Nodes { get; }
    public IReadOnlyList<GraphEdge> Edges { get; }

    internal GraphValue(ProtoGraph graph)
    {
        Nodes = Array.AsReadOnly(graph.Nodes.Select(node => new GraphNode(node)).ToArray());
        Edges = Array.AsReadOnly(graph.Edges.Select(edge => new GraphEdge(edge)).ToArray());
    }
}
