/**
 * 使用广度优先搜索算法查找从传送点到目标的所有路径
 * @param {Object} nodeData - 节点数据
 * @param {Object} targetNode - 目标节点
 * @param {Object} nodeMap - 节点映射
 * @returns {Array} 找到的所有可行路径
 */
this.breadthFirstPathSearch =
function (nodeData, targetNode, nodeMap) {
    // 存储找到的所有有效路径
    const validPaths = [];

    // 获取所有传送点作为起点
    const teleportNodes = nodeData.node.filter(node => node.type === "teleport");
    log.debug(`找到 ${teleportNodes.length} 个传送点作为可能的起点`);

    // 对每个传送点，尝试查找到目标的路径
    for (const startNode of teleportNodes) {
        // 初始化队列，每个元素包含 [当前节点, 路径信息]
        const queue = [[startNode, {
            startNode: startNode,
            routes: [],
            visitedNodes: new Set([startNode.id])
        }]];

        // 广度优先搜索
        while (queue.length > 0) {
            const [currentNode, pathInfo] = queue.shift();

            // 如果已经到达目标节点
            if (currentNode.id === targetNode.id) {
                validPaths.push({
                    startNode: pathInfo.startNode,
                    targetNode: targetNode,
                    routes: [...pathInfo.routes]
                });
                continue; // 找到一条路径，继续搜索其他可能路径
            }

            // 检查当前节点的下一个连接
            if (currentNode.next && currentNode.next.length > 0) {
                for (const nextRoute of currentNode.next) {
                    const nextNodeId = nextRoute.target;

                    // 避免循环
                    if (pathInfo.visitedNodes.has(nextNodeId)) {
                        continue;
                    }

                    const nextNode = nodeMap[nextNodeId];
                    if (!nextNode) {
                        continue;
                    }

                    // 创建新的路径信息
                    const newPathInfo = {
                        startNode: pathInfo.startNode,
                        routes: [...pathInfo.routes, nextRoute.route],
                        visitedNodes: new Set([...pathInfo.visitedNodes, nextNodeId])
                    };

                    // 加入队列
                    queue.push([nextNode, newPathInfo]);
                }
            }
        }
    }

    // 检查是否存在反向路径
    const reversePaths = findReversePathsIfNeeded(nodeData, targetNode, nodeMap, validPaths);
    validPaths.push(...reversePaths);

    log.debug(`共找到 ${validPaths.length} 条有效路径`);
    return validPaths;
}