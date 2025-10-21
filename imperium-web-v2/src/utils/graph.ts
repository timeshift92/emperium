export type Node = { id: string; label: string; group?: string };
export type Edge = { id: string; source: string; target: string; weight?: number };
export type LayoutNode = Node & { x: number; y: number };
export type LayoutEdge = Edge;

/**
 * Построение симплифицированного force-directed layout.
 * Использует итеративное приближение с ленточными силами.
 */
export function layoutGraph(nodes: Node[], edges: Edge[], iterations = 80): { nodes: LayoutNode[]; edges: LayoutEdge[] } {
  if (nodes.length === 0) return { nodes: [], edges: [] };

  const width = 260;
  const height = 240;
  const positions = new Map<string, { x: number; y: number; vx: number; vy: number }>();
  for (const node of nodes) {
    const angle = Math.random() * Math.PI * 2;
    positions.set(node.id, { x: width / 2 + Math.cos(angle) * width / 4, y: height / 2 + Math.sin(angle) * height / 4, vx: 0, vy: 0 });
  }

  const edgeLookup = edges.reduce((acc, edge) => {
    if (!acc.has(edge.source)) acc.set(edge.source, new Set<string>());
    if (!acc.has(edge.target)) acc.set(edge.target, new Set<string>());
    acc.get(edge.source)!.add(edge.target);
    acc.get(edge.target)!.add(edge.source);
    return acc;
  }, new Map<string, Set<string>>());

  const dt = 0.02;
  const repulsion = 300;
  const spring = 0.1;

  for (let i = 0; i < iterations; i++) {
    for (const nodeA of nodes) {
      const posA = positions.get(nodeA.id)!;
      for (const nodeB of nodes) {
        if (nodeA.id === nodeB.id) continue;
        const posB = positions.get(nodeB.id)!;
        let dx = posA.x - posB.x;
        let dy = posA.y - posB.y;
        let distSq = dx * dx + dy * dy;
        if (distSq === 0) {
          dx = (Math.random() - 0.5) * 0.1;
          dy = (Math.random() - 0.5) * 0.1;
          distSq = dx * dx + dy * dy;
        }
        const dist = Math.sqrt(distSq);
        const force = repulsion / distSq;
        posA.vx += (dx / dist) * force * dt;
        posA.vy += (dy / dist) * force * dt;
      }
    }

    for (const edge of edges) {
      const posA = positions.get(edge.source)!;
      const posB = positions.get(edge.target)!;
      const dx = posB.x - posA.x;
      const dy = posB.y - posA.y;
      const dist = Math.sqrt(dx * dx + dy * dy) || 0.0001;
      const desired = 70;
      const force = (dist - desired) * spring * dt;
      const fx = (dx / dist) * force;
      const fy = (dy / dist) * force;
      posA.vx += fx;
      posA.vy += fy;
      posB.vx -= fx;
      posB.vy -= fy;
    }

    for (const node of nodes) {
      const pos = positions.get(node.id)!;
      pos.vx *= 0.9;
      pos.vy *= 0.9;
      pos.x = Math.min(width, Math.max(0, pos.x + pos.vx * dt));
      pos.y = Math.min(height, Math.max(0, pos.y + pos.vy * dt));
    }
  }

  const layoutNodes: LayoutNode[] = nodes.map(node => {
    const pos = positions.get(node.id)!;
    return { ...node, x: pos.x, y: pos.y };
  });
  return { nodes: layoutNodes, edges };
}

export function buildRelationshipGraph(
  centerId: string,
  relationships: { otherId: string; trust: number; love: number; hostility: number; otherName: string }[]
) {
  const nodes: Node[] = [{ id: centerId, label: "Вы" }];
  const edges: Edge[] = [];

  for (const rel of relationships) {
    nodes.push({ id: rel.otherId, label: rel.otherName });
    const weight = Math.abs(rel.trust) + Math.abs(rel.love) + Math.abs(rel.hostility);
    edges.push({ id: `${centerId}-${rel.otherId}`, source: centerId, target: rel.otherId, weight });
  }

  return layoutGraph(nodes, edges);
}
