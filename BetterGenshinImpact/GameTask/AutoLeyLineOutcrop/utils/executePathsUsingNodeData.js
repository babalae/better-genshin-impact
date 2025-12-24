/**
 * 使用节点数据执行路径
 * @param {Object} position - 位置对象
 * @returns {Promise<void>}
 */
this.executePathsUsingNodeData = async function (position) {
    try {
        const nodeData = await loadNodeData();
        let currentNodePosition = position;
        const targetNode = findTargetNodeByPosition(nodeData, currentNodePosition.x, currentNodePosition.y);

        if (!targetNode) {
            log.error(`未找到与坐标(${currentNodePosition.x}, ${currentNodePosition.y})匹配的目标节点`);
            await ensureExitRewardPage();
            return;
        }
        log.debug(`找到目标节点: ID ${targetNode.id}, 位置(${targetNode.position.x}, ${targetNode.position.y})`);
        const paths = findPathsToTarget(nodeData, targetNode);

        if (paths.length === 0) {
            log.error(`未找到通向目标节点(ID: ${targetNode.id})的路径`);
            await ensureExitRewardPage();
            return;
        }

        // 选择最短的路径执行
        const optimalPath = selectOptimalPath(paths);
        log.debug(`选择了含有 ${optimalPath.routes.length} 个路径点的最优路径`);

        // 执行路径
        await executePath(optimalPath);
        currentRunTimes++;

        // 如果达到刷取次数上限，退出循环
        if (currentRunTimes >= settings.timesValue) {
            return;
        }
        let currentNode = targetNode;
        log.debug(`开始处理节点链，目标节点ID: ${targetNode.id}, next数量: ${targetNode.next ? targetNode.next.length : 'undefined'}`);

        while (currentNode.next && currentRunTimes < settings.timesValue) {
            log.debug(`当前节点ID: ${currentNode.id}, next数量: ${currentNode.next.length}`);
            if (currentNode.next.length === 1) {                // 获取下一个节点的ID 和 路径，并在节点数据中找到下一个节点
                const nextNodeId = currentNode.next[0].target;
                const nextRoute = currentNode.next[0].route;
                log.debug(`单一路径: 从节点${currentNode.id}到节点${nextNodeId}, 路径: ${nextRoute}`);
                const nextNode = nodeData.node.find(node => node.id === nextNodeId);

                if (!nextNode) {
                    await ensureExitRewardPage();
                    return;
                }
                const pathObject = {
                    startNode: currentNode,
                    targetNode: nextNode,
                    routes: [nextRoute]
                };

                log.info(`直接执行下一个节点路径: ${nextRoute}`);
                await executePath(pathObject);

                currentRunTimes++;

                log.info(`完成节点 ID ${nextNodeId}, 已执行 ${currentRunTimes}/${settings.timesValue} 次`);                // 更新当前节点为下一个节点，继续检查
                currentNode = nextNode;
                currentNodePosition = { x: nextNode.position.x, y: nextNode.position.y };
            }            
            else if (currentNode.next.length > 1) {
                // 如果存在分支路线，先打开大地图判断下一个地脉花的位置，然后结合顺序边缘数据选择最优路线
                log.info("检测到多个分支路线，开始查找下一个地脉花位置");

                // 备份当前地脉花坐标
                const currentLeyLineX = leyLineX;
                const currentLeyLineY = leyLineY;

                // 打开大地图
                await genshin.returnMainUi();
                keyPress("M");
                await sleep(1000);

                // 查找下一个地脉花
                const found = await locateLeyLineOutcrop(settings.leyLineOutcropType);
                await genshin.returnMainUi();

                if (!found) {
                    log.warn("无法在分支点找到下一个地脉花，退出本次循环");
                    await ensureExitRewardPage();
                    return;
                }                
                log.info(`找到下一个地脉花，位置: (${leyLineX}, ${leyLineY})`);

                // 直接比较所有分支节点到地脉花的距离，选择最近的路径
                let selectedRoute = null;
                let selectedNodeId = null;
                let closestDistance = Infinity;
                for (const nextRoute of currentNode.next) {
                    const branchNodeId = nextRoute.target;
                    const branchNode = nodeData.node.find(node => node.id === branchNodeId);

                    if (!branchNode) continue;

                    const distance = calculate2DDistance(
                        leyLineX, leyLineY,
                        branchNode.position.x, branchNode.position.y
                    );

                    log.info(`分支节点ID ${branchNodeId} 到地脉花距离: ${distance.toFixed(2)}`);

                    if (distance < closestDistance) {
                        closestDistance = distance;
                        selectedRoute = nextRoute.route;
                        selectedNodeId = branchNodeId;
                    }
                }

                if (!selectedRoute) {
                    log.error("无法找到合适的路线，终止执行");
                    // 恢复原始坐标
                    leyLineX = currentLeyLineX;
                    leyLineY = currentLeyLineY;
                    await ensureExitRewardPage();
                    return;
                }                
                const nextNode = nodeData.node.find(node => node.id === selectedNodeId);
                if (!nextNode) {
                    log.error(`未找到节点ID ${selectedNodeId}，终止执行`);
                    // 恢复原始坐标
                    leyLineX = currentLeyLineX;
                    leyLineY = currentLeyLineY;
                    await ensureExitRewardPage();
                    return;
                }

                log.info(`选择路线: ${selectedRoute}, 目标节点ID: ${selectedNodeId}`);

                // 创建路径对象并执行
                const pathObject = {
                    startNode: currentNode,
                    targetNode: nextNode,
                    routes: [selectedRoute]
                };
                await executePath(pathObject);
                currentRunTimes++;
                log.info(`完成节点 ID ${selectedNodeId}, 已执行 ${currentRunTimes}/${settings.timesValue} 次`);
                // 更新当前节点为下一个节点，继续检查
                currentNode = nextNode;
                currentNodePosition = { x: nextNode.position.x, y: nextNode.position.y };
            }
            else {
                log.info("当前路线完成，退出循环");
                break;
            }
        }
    }
    catch (error) {
        if(error.message.includes("战斗失败")) {
            consecutiveFailureCount++;
            log.error(`战斗失败，连续失败次数: ${consecutiveFailureCount}/${MAX_CONSECUTIVE_FAILURES}`);
            
            // 检查是否超过最大连续失败次数
            if (consecutiveFailureCount >= MAX_CONSECUTIVE_FAILURES) {
                await ensureExitRewardPage();
                throw new Error(`连续战斗失败${MAX_CONSECUTIVE_FAILURES}次，可能是队伍配置不足以完成挑战，脚本终止`);
            }
            
            await ensureExitRewardPage();
            // processResurrect()已在processLeyLineOutcrop中调用，这里直接return
            // return后会回到runLeyLineChallenges的while循环，重新寻找地脉花
            log.info("将重新寻找地脉花并重试");
            return;
        }
        // 其他错误需要向上传播
        log.error(`执行路径时出错: ${error.message}`);
        await ensureExitRewardPage();
        throw error;
    }
}