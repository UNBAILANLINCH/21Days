"""只读实际Unity网格，一次性离线接触诊断；不改几何/幅度/模型。
5秒锚点终端完成及eye-contact.json时间；完成即退出。
复用现有numpy及诊断JSON工具；第一次round没有三角索引，需要本轮原始网格。
"""
import numpy as np
from analysis.laila_v2_candidate.capture_diagnostics import ROOT, read_json, write_json

OUT = ROOT / 'artifacts/laila_v2_candidate/lifecycle-eye-20261003'


def segment_hits(start, end, triangle):
    direction = end - start
    e1 = triangle[:, 1] - triangle[:, 0]; e2 = triangle[:, 2] - triangle[:, 0]
    p = np.cross(direction, e2); det = np.sum(e1 * p, axis=1)
    valid = np.abs(det) > 1e-15
    inv = np.divide(1., det, out=np.zeros_like(det), where=valid)
    offset = start - triangle[:, 0]
    u = np.sum(offset * p, axis=1) * inv
    q = np.cross(offset, e1)
    v = np.sum(direction * q, axis=1) * inv
    t = np.sum(e2 * q, axis=1) * inv
    return valid & (u >= -1e-7) & (v >= -1e-7) & (u + v <= 1 + 1e-7) & (t >= -1e-7) & (t <= 1 + 1e-7)


def intersections(a, b, pair_filter=None):
    # 包围盒后双向6条边的非共面segment-triangle检测，包含边界接触。
    amin, amax = a.min(1), a.max(1); bmin, bmax = b.min(1), b.max(1)
    ia, ib = np.where(np.all((amax[:, None] >= bmin[None] - 1e-9) & (bmax[None] >= amin[:, None] - 1e-9), axis=2))
    if pair_filter is not None:
        keep = pair_filter(ia, ib); ia, ib = ia[keep], ib[keep]
    if not len(ia): return np.empty((0, 2), dtype=int), 0
    aa, bb = a[ia], b[ib]; hits = np.zeros(len(ia), dtype=bool)
    for i, j in [(0, 1), (1, 2), (2, 0)]:
        hits |= segment_hits(aa[:, i], aa[:, j], bb) | segment_hits(bb[:, i], bb[:, j], aa)
    na = np.cross(aa[:, 1] - aa[:, 0], aa[:, 2] - aa[:, 0])
    nb = np.cross(bb[:, 1] - bb[:, 0], bb[:, 2] - bb[:, 0])
    lengths = np.linalg.norm(na, axis=1) * np.linalg.norm(nb, axis=1)
    parallel = np.linalg.norm(np.cross(na, nb), axis=1) <= 1e-8 * lengths
    distance = np.abs(np.sum(na * (bb[:, 0] - aa[:, 0]), axis=1))
    coplanar = parallel & (distance <= 1e-9 * np.linalg.norm(na, axis=1))
    return np.column_stack([ia[hits], ib[hits]]), int(coplanar.sum())


def main():
    data = read_json(OUT / 'eye-sweep.json')
    if not data.get('completed') or data.get('error'): raise ValueError('Unity眼睑采样未完成')
    baseline = np.array(data['baseline_world_vertices']); triangles = np.array(data['triangles']).reshape(-1, 3)
    last = data['entries'][-1]; affected = np.array([int(d[0]) for d in last['deltas_world']])
    incident = np.flatnonzero(np.isin(triangles, affected).any(1))
    eyes = [(e['name'], np.array(e['vertices_world'])[np.array(e['triangles']).reshape(-1, 3)]) for e in data['eyes']]
    _, weld_ids = np.unique(np.round(baseline, 7), axis=0, return_inverse=True)
    welded = weld_ids[triangles]
    def nonadjacent(ia, ib):
        return ~np.any(welded[incident[ia]][:, :, None] == welded[ib][:, None, :], axis=(1, 2))
    rows, base_pairs, base_self = [], {}, set()
    for entry in data['entries']:
        vertices = baseline.copy()
        for d in entry['deltas_world']: vertices[int(d[0])] += d[1:]
        a = vertices[triangles[incident]]
        eye_rows = []
        for name, b in eyes:
            pairs, coplanar = intersections(a, b)
            current = {(int(incident[i]), int(j)) for i, j in pairs}
            if entry['weight'] == 0: base_pairs[name] = current
            newly = current - base_pairs[name]
            eye_rows.append({'name': name, 'noncoplanar_contact_pairs': len(current),
                             'new_vs_basis_pairs': len(newly), 'coplanar_bbox_candidates_unresolved': coplanar,
                             'pairs': sorted(current), 'new_pairs': sorted(newly)})
        self_pairs, self_coplanar = intersections(a, vertices[triangles], nonadjacent)
        current_self = {tuple(sorted((int(incident[i]), int(j)))) for i, j in self_pairs}
        if entry['weight'] == 0: base_self = current_self
        rows.append({'weight': entry['weight'], 'id': entry['id'], 'normal_alerts': entry['orientation_alerts'], 'eyes': eye_rows,
                     'nonadjacent_face_contacts': len(current_self), 'new_face_contacts': sorted(current_self - base_self),
                     'coplanar_nonadjacent_face_bbox_candidates_unresolved': self_coplanar})
    result = {'method': 'world-space bounded-box + bidirectional segment-triangle, inclusive boundary',
              'incident_triangles': len(incident), 'rows': rows,
              'limitations': ['边界接触与穿透需结合面位置/截图解释。', '共面包围盒候选单列，未判穿透。',
                             '只检查受影响眼睑邻接面和场景现有眼部表面，不保证封闭体体积或全部自交。']}
    write_json(OUT / 'eye-contact.json', result)
    summary = lambda row: [{k: e[k] for k in ['name', 'noncoplanar_contact_pairs', 'new_vs_basis_pairs',
                                             'coplanar_bbox_candidates_unresolved']} for e in row['eyes']]
    print({'incident_triangles': len(incident), 'first_alert_weight': next((r['weight'] for r in rows if r['normal_alerts']), None),
           'baseline_contacts': summary(rows[0]), 'max_contacts': summary(rows[-1]),
           'max_new_nonadjacent_face_contacts': max(len(r['new_face_contacts']) for r in rows),
           'max_coplanar_face_candidates': max(r['coplanar_nonadjacent_face_bbox_candidates_unresolved'] for r in rows)})


if __name__ == '__main__': main()
