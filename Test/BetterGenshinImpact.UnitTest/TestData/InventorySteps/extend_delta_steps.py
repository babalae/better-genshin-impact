"""基于第六步时间追加无库存基准回归数据，不改写 OCR 原始证据。"""
from pathlib import Path
from datetime import datetime, timedelta
import copy
import json
import zipfile


def extend(root):
    """同步步骤目录、导入副本与压缩包，并使旧验证结果失效。"""
    root = Path(root)
    template = json.loads((root / 'steps/06-final-verification.json').read_text(encoding='utf-8'))
    uid, body = next(iter(template['accounts'].items()))
    start = max(datetime.fromisoformat(r[0]['recordedAt'].replace('Z', '+00:00')) for r in body['inventory'].values())
    names = ['甜甜花', '薄荷', '日落果']
    # 兼容生成器输出的根目录 baseline 与已整理到 steps 的现有数据。
    baseline_path = root / '00-baseline.json'
    if baseline_path.exists():
        (root / 'steps/00-baseline.json').write_bytes(baseline_path.read_bytes())
    baseline = json.loads((root / 'steps/00-baseline.json').read_text(encoding='utf-8'))
    assert not set(names) & baseline['accounts'][uid]['inventory'].keys()
    sources = ['Test.Ocr168.Gain', 'Test.Ocr168.Spend']
    changes = [dict(name=name, delta=value, recordedAt=(start + timedelta(minutes=i + 1)).isoformat(),
                    source=sources[value < 0])
               for i, (name, value) in enumerate(zip([*names, names[2]], [5, -3, 2, -2]))]
    for filename, inventory, deltas, note in [
        ('07-no-baseline-deltas.json', {}, changes + [changes[0]],
         '4条无基准增量+1条重复；甜甜花（+5）、薄荷（-3）、日落果（±0）。不建立确认库存；JSON导出不包含这三项或deltas。'),
        ('08-first-inventory.json', {name: [dict(quantity=value, source='Test.Ocr168.Scan',
          recordedAt=(start + timedelta(minutes=10)).isoformat())] for name, value in zip(names, [5, 10, 20])}, None,
         '首次确认库存5、10、20，4条增量完成；VerifiedCorrect为空，不以零基准判断一致或不一致。导出仅保留确认库存。')]:
        package = copy.deepcopy(template)
        package['note'] = note
        package['itemDefinitions'] = {}
        package['accounts'][uid] = dict(inventory=inventory)
        if deltas is not None:
            package['accounts'][uid]['deltas'] = deltas
        content = json.dumps(package, ensure_ascii=False, indent=2) + '\n'
        for folder in ['steps', 'Inventory', 'import-testcase']:
            (root / folder).mkdir(exist_ok=True)
            (root / folder / filename).write_text(content, encoding='utf-8')
    # 同一物品叠加五类情况；最后不再核实，让人工检查时保留全部提示。
    resin = '须臾树脂'
    now = datetime.fromisoformat(template['exportedAt'].replace('Z', '+00:00'))
    def record(quantity, minute, expirations=None):
        value = dict(quantity=quantity, recordedAt=(start + timedelta(minutes=minute)).isoformat(), source='Test.Ocr168.Scan')
        if expirations is not None:
            value['expirations'] = expirations
        return [value]
    def delta(quantity, minute, expirations=None):
        value = dict(name=resin, delta=quantity, recordedAt=(start + timedelta(minutes=minute)).isoformat(), source='Test.Ocr168.Gain' if quantity > 0 else 'Test.Ocr168.Spend')
        if expirations is not None:
            value['expirations'] = expirations
        return value
    def expiry(days, quantity=1):
        return dict(quantity=quantity, expiresAt=(now + timedelta(days=days)).isoformat())
    for filename, inventory, deltas, definitions in [
        ('09-extreme-errors.json', {resin: record(-1, 11)}, [delta(-99999, 12)], {}),
        ('10-extreme-mixed.json', {resin: record(3, 13, [expiry(-1/24), expiry(4), expiry(9)])},
         [delta(2, 14, [expiry(6, 2)]), delta(-1, 15)], {resin: dict(category='测试错误分类')}),
        ('11-extreme-all-statuses.json', {resin: record(-1, 16)},
         [delta(-99999, 17), delta(1, 18, [expiry(7, 2)])], {})]:
        package = copy.deepcopy(template)
        package['note'] = '极端多事件回归：按09→10→11导入，期间不归档；最终须臾树脂同时保留五类提示。'
        package['itemDefinitions'] = definitions
        package['accounts'][uid] = dict(inventory=inventory, deltas=deltas)
        (root / 'steps' / filename).write_text(json.dumps(package, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    expected_path = root / 'expected.json'
    expected = json.loads(expected_path.read_text(encoding='utf-8'))
    expected.update(step7Pending=4, step7Duplicates=1, step7WithoutRecords=dict(zip(names, [5, -3, 0])),
                    step8Quantities=dict(zip(names, [5, 10, 20])), step8CompletedDeltas=15,
                    step11Item=resin, step11CardCount=5, step11PendingDelta=1)
    expected_path.write_text(json.dumps(expected, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    # 同步整套步骤，避免重新生成后导入副本混用旧时间。
    for path in (root / 'steps').glob('*.json'):
        for folder in ['Inventory', 'import-testcase']:
            (root / folder / path.name).write_bytes(path.read_bytes())
    # 按现有目录打包，避免压缩包继续遗漏新增步骤。
    with zipfile.ZipFile(root / 'inventory.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted((root / 'Inventory').glob('*.json')):
            archive.write(path, 'Inventory/' + path.name)
    (root / 'validation-status.json').write_text(json.dumps(dict(status='not_run', passed=False,
        reason='已重新生成含09至11极端多事件步骤的数据；待执行完整服务验证。'), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    extend(Path(__file__).resolve().parent)
