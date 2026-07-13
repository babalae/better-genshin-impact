document.addEventListener('DOMContentLoaded', function() {
    document.querySelectorAll('th').forEach(function(th) {
        th.removeAttribute('onclick');
        th.addEventListener('click', function() {
            const table = this.closest('table');
            const columnIndex = Array.from(this.parentNode.children).indexOf(this);
            const sortType = this.getAttribute('data-sort-type') || 'string';
            sortTable(table, columnIndex, sortType);
        });
    });
});

function getCellValue(row, columnIndex, sortType) {
    try {
        if (!row || !row.cells || columnIndex >= row.cells.length) {
            return sortType === 'number' || sortType === 'date' ? 0 : '';
        }

        const cell = row.cells[columnIndex];
        if (!cell) return sortType === 'number' || sortType === 'date' ? 0 : '';

        // 优先使用data-sort属性值
        const sortValue = cell.getAttribute('data-sort');
        if (sortValue !== null) {
            return sortType === 'number' || sortType === 'date' ? parseFloat(sortValue) : sortValue;
        }

        const value = cell.textContent ? cell.textContent.trim() : '';

        // 根据排序类型转换值
        if (sortType === 'number') {
            // 提取数字部分
            const numMatch = value.match(/[\d\.]+/);
            return numMatch ? parseFloat(numMatch[0]) : 0;
        } else if (sortType === 'date') {
            // 修改日期解析逻辑，优先处理 yyyy-MM-dd 格式
            if (!value) return 0;

            // 尝试解析 yyyy-MM-dd 格式
            const dateOnlyMatch = value.match(/^(\d{4})-(\d{2})-(\d{2})$/);
            if (dateOnlyMatch) {
                const year = parseInt(dateOnlyMatch[1]);
                const month = parseInt(dateOnlyMatch[2]) - 1; // 月份从0开始
                const day = parseInt(dateOnlyMatch[3]);
                return new Date(year, month, day).getTime();
            }

            // 尝试解析标准日期时间格式 yyyy-MM-dd HH:mm:ss
            const dateTimeMatch = value.match(/(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2})/);
            if (dateTimeMatch) {
                const year = parseInt(dateTimeMatch[1]);
                const month = parseInt(dateTimeMatch[2]) - 1; // 月份从0开始
                const day = parseInt(dateTimeMatch[3]);
                const hour = parseInt(dateTimeMatch[4]);
                const minute = parseInt(dateTimeMatch[5]);
                const second = parseInt(dateTimeMatch[6]);
                return new Date(year, month, day, hour, minute, second).getTime();
            }

            // 如果无法解析，尝试直接使用Date构造函数
            return new Date(value).getTime() || 0;
        } else if (sortType === 'time') {
            // 处理时间格式（小时、分钟、秒）
            let seconds = 0;
            if (value.includes('小时')) {
                const hoursMatch = value.match(/(\d+)小时/);
                if (hoursMatch) {
                    seconds += parseInt(hoursMatch[1]) * 3600;
                }
            }
            if (value.includes('分钟')) {
                const minutesMatch = value.match(/(\d+)分钟/);
                if (minutesMatch) {
                    seconds += parseInt(minutesMatch[1]) * 60;
                }
            }
            if (value.includes('秒')) {
                const secondsMatch = value.match(/([\d\.]+)秒/);
                if (secondsMatch) {
                    seconds += parseFloat(secondsMatch[1]);
                }
            }
            return seconds;
        }
        return value;
    } catch (e) {
        console.error('获取单元格值时出错:', e);
        return sortType === 'number' || sortType === 'date' ? 0 : '';
    }
}

function sortTable(table, columnIndex, sortType) {
    let loadingDiv = null;
    let loadingTimer = null;

    try {
        if (!table) return;
        const tbody = table.querySelector('tbody');
        if (!tbody) return;

        // 创建排序中的提示，但不立即显示
        loadingDiv = document.createElement('div');
        loadingDiv.style.position = 'fixed';
        loadingDiv.style.top = '50%';
        loadingDiv.style.left = '50%';
        loadingDiv.style.transform = 'translate(-50%, -50%)';
        loadingDiv.style.padding = '20px';
        loadingDiv.style.background = 'rgba(0,0,0,0.7)';
        loadingDiv.style.color = 'white';
        loadingDiv.style.borderRadius = '5px';
        loadingDiv.style.zIndex = '1000';
        loadingDiv.textContent = '排序中，请稍候...';

        // 设置延迟显示提示，只有排序超过500毫秒才显示
        loadingTimer = setTimeout(function() {
            document.body.appendChild(loadingDiv);
        }, 1000);

        // 使用setTimeout让UI有机会更新
        setTimeout(function() {
            try {
                // 保存汇总行
                const summaryRows = Array.from(tbody.querySelectorAll('tr.ignore-sort') || []);

                // 获取所有行并创建映射
                const allRows = Array.from(tbody.querySelectorAll('tr') || []);
                if (!allRows.length) {
                    clearTimeout(loadingTimer);
                    if (loadingDiv && loadingDiv.parentNode) {
                        document.body.removeChild(loadingDiv);
                    }
                    return;
                }

                // 首先标记所有行
                for (let i = 0; i < allRows.length; i++) {
                    if (allRows[i]) {
                        allRows[i].setAttribute('data-original-index', i.toString());
                    }
                }

                // 获取需要排序的行（排除汇总行和子行）
                const rows = [];
                for (let i = 0; i < allRows.length; i++) {
                    const row = allRows[i];
                    if (row && row.classList &&
                        !row.classList.contains('ignore-sort') &&
                        !row.classList.contains('sub-row')) {
                        rows.push(row);
                    }
                }

                // 创建行和其对应的附属行的映射
                const rowPairs = [];
                for (let i = 0; i < rows.length; i++) {
                    try {
                        const row = rows[i];
                        if (!row || !row.getAttribute) continue;

                        const originalIndexStr = row.getAttribute('data-original-index');
                        if (!originalIndexStr) continue;

                        const originalIndex = parseInt(originalIndexStr);
                        if (isNaN(originalIndex)) continue;

                        // 安全地获取下一行，确保它存在
                        let nextRow = null;
                        if (originalIndex + 1 < allRows.length) {
                            nextRow = allRows[originalIndex + 1];
                        }

                        // 安全地检查nextRow是否存在且是否有classList属性
                        if (nextRow && nextRow.classList &&
                            typeof nextRow.classList.contains === 'function' &&
                            nextRow.classList.contains('sub-row')) {
                            rowPairs.push({main: row, sub: nextRow});
                        } else {
                            rowPairs.push({main: row, sub: null});
                        }
                    } catch (e) {
                        console.error('创建行对时出错:', e);
                        continue;
                    }
                }

                // 确定排序方向
                let sortDirection = 'asc';
                const headerCells = table.querySelectorAll('th');
                if (!headerCells || columnIndex >= headerCells.length) {
                    if (loadingDiv && loadingDiv.parentNode) {
                        document.body.removeChild(loadingDiv);
                    }
                    return;
                }

                const headerCell = headerCells[columnIndex];
                if (!headerCell || !headerCell.classList) {
                    if (loadingDiv && loadingDiv.parentNode) {
                        document.body.removeChild(loadingDiv);
                    }
                    return;
                }

                // 如果已经按这列排序，则切换方向
                if (headerCell.classList.contains('sort-asc')) {
                    sortDirection = 'desc';
                } else if (headerCell.classList.contains('sort-desc')) {
                    sortDirection = 'asc';
                }

                // 清除所有表头的排序指示器
                for (let i = 0; i < headerCells.length; i++) {
                    const th = headerCells[i];
                    if (th && th.classList) {
                        th.classList.remove('sort-asc', 'sort-desc');
                    }
                }

                // 添加新的排序指示器
                headerCell.classList.add('sort-' + sortDirection);

                // 特殊处理耗时列
                const isTimeColumn = headerCell.textContent && headerCell.textContent.trim() === '任务耗时';
                const actualSortType = isTimeColumn ? 'time' : sortType;

                // 排序行对 - 使用稳定的排序算法
                rowPairs.sort((pairA, pairB) => {
                    try {
                        // 确保main对象存在
                        if (!pairA || !pairA.main || !pairB || !pairB.main) {
                            return 0;
                        }

                        const valueA = getCellValue(pairA.main, columnIndex, actualSortType);
                        const valueB = getCellValue(pairB.main, columnIndex, actualSortType);

                        let result;
                        if (actualSortType === 'number' || actualSortType === 'date' || actualSortType === 'time') {
                            result = sortDirection === 'asc' ? valueA - valueB : valueB - valueA;
                        } else {
                            result = sortDirection === 'asc'
                                ? String(valueA).localeCompare(String(valueB), 'zh-CN')
                                : String(valueB).localeCompare(String(valueA), 'zh-CN');
                        }

                        // 如果值相等，保持原始顺序（稳定排序）
                        if (result === 0) {
                            const indexA = parseInt(pairA.main.getAttribute('data-original-index') || '0');
                            const indexB = parseInt(pairB.main.getAttribute('data-original-index') || '0');
                            return indexA - indexB;
                        }

                        return result;
                    } catch (e) {
                        console.error('排序比较时出错:', e);
                        return 0;
                    }
                });

                // 创建文档片段以提高性能
                const fragment = document.createDocumentFragment();

                // 先添加排序后的数据行和附属行
                for (let i = 0; i < rowPairs.length; i++) {
                    const pair = rowPairs[i];
                    // 确保main对象存在
                    if (pair && pair.main) {
                        fragment.appendChild(pair.main);
                        // 确保sub对象存在
                        if (pair.sub) {
                            fragment.appendChild(pair.sub);
                        }
                    }
                }

                // 最后添加汇总行
                for (let i = 0; i < summaryRows.length; i++) {
                    const row = summaryRows[i];
                    if (row) {
                        fragment.appendChild(row);
                    }
                }

                // 清空tbody
                while (tbody.firstChild) {
                    tbody.removeChild(tbody.firstChild);
                }

                // 一次性添加所有行
                tbody.appendChild(fragment);
            } catch (error) {
                console.error('排序过程中发生错误:', error);
                alert('排序过程中发生错误: ' + error.message);
            } finally {
                // 清除定时器并移除加载提示
                clearTimeout(loadingTimer);
                if (loadingDiv && loadingDiv.parentNode) {
                    document.body.removeChild(loadingDiv);
                }
            }
        }, 50); // 短暂延迟让UI更新
    } catch (error) {
        console.error('排序初始化时发生错误:', error);
        // 清除定时器并确保加载提示被移除
        clearTimeout(loadingTimer);
        if (loadingDiv && loadingDiv.parentNode) {
            document.body.removeChild(loadingDiv);
        }
    }
}
function togglePre(preId, btn) {
    const pre = document.getElementById(preId);
    if (!pre) {
        console.error(`未找到 ID 为 "${preId}" 的元素`);
        return;
    }

    if (window.getComputedStyle(pre).display === "none") {
        pre.style.display = "block";
        btn.textContent = "隐藏 JSON";
    } else {
        pre.style.display = "none";
        btn.textContent = "显示 JSON";
    }
}

function copyPreContent(preId) {
    const pre = document.getElementById(preId);
    if (!pre) {
        console.error(`未找到 ID 为 "${preId}" 的元素`);
        return;
    }

    const docComment = `
// 如果所有 JSON 都在同一目录下，可直接拷贝并命名为 control.json5，放入该追踪目录。
// 注意：有些因为卡死或其他原因导致失败的记录需自行判断处理。
// 参数说明：
// primary_target: " 
//   主目标，值为 elite或normal 时，所配置的类别达到上限时，就会跳过该路径。
//   填写 disable 表示非锄地脚本（如挖矿战斗），也会纳入统计，即使达到上限，但不影响继续执行。
//   如果不填或其他值，则两种都达到上限（另一种目标数为0也会跳过）才会跳过。
// global_cover: 针对该目录所有 JSON。
// json_list.cover: name 与实际文件名匹配的 JSON。
// allow_farming_count: true 开启锄地规划时纳入统计。
// enable_monster_loot_split: true 允许区分怪物拾取，支持调度器只拾取精英配置，这里把精英为0的直接启用了。
// normal_mob_count: 小怪计数。
// elite_mob_count: 精英计数。
// duration_seconds: 执行秒数。
// elite_details: 精英详细。
// total_mora: 摩拉数。

`;

    const text = docComment + pre.textContent;

    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text)
            .then(() => alert("已复制到剪贴板！"))
            .catch(err => console.error("复制失败：", err));
    } else {
        const textarea = document.createElement("textarea");
        textarea.value = text;
        document.body.appendChild(textarea);
        textarea.select();
        try {
            document.execCommand("copy");
            alert("已复制到剪贴板！");
        } catch (err) {
            console.error("复制失败：", err);
        }
        document.body.removeChild(textarea);
    }
}