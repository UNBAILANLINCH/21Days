"""最新复评中泄漏、拒识分解与简单基线的数值护栏。"""
import numpy as np
import pytest

from analysis.laila_v2_candidate.latest_review import audit_rows, partial_calibration, centroid_logits, feature_conflicts
from analysis.laila_v2_candidate.retrain_candidate import score


def fixture():
    return {'id':np.array(list('abcdefg')), 'y5':np.array([0,1,2,3,4,3,-1]),
            'supervised5_mask':np.array([1,1,1,1,1,1,0],dtype=bool),
            'group':np.array(['train']*5+['cal','other']),
            'raw17':np.arange(119,dtype=np.float32).reshape(7,17),
            'label_original':np.array(['neutral','happy','sad','fear','angry','surprise','ambiguous']),
            'confused_candidate_mask':np.zeros(7,dtype=bool)}


def test_wrong_acceptance_correct_rejection_and_training_membership():
    arrays = fixture()
    z = np.zeros((7,5),dtype=np.float32)
    z[:,0] = 4
    result = audit_rows(score(z,arrays,1,-100,.4),arrays,['a'],{'a':'happy','b':'sad'})
    assert result['correct_but_rejected'] == 1
    assert result['per_class']['neutral']['correct_but_rejected'] == 1
    assert result['known_wrong_accepted'] == 0
    assert result['rows'][0]['in_candidate_training']
    assert result['rows'][0]['label_changed_since_training']
    assert not result['rows'][1]['in_candidate_training']
    assert result['rows'][1]['source_group_seen_in_training']
    assert not result['rows'][1]['label_changed_since_training']
    assert result['rows'][0]['same_raw17_as_train']
    assert result['rows'][1]['nearest_train_raw17_distance'] > 0


def test_calibration_only_uses_calibration_logits_and_never_claims_full_validation():
    arrays = fixture()
    train = np.arange(7)<5
    cal = np.arange(7)==5
    z = np.zeros((7,5))
    first = partial_calibration(z,arrays,train,cal,1.)
    z[~cal] = 10000
    second = partial_calibration(z,arrays,train,cal,1.)
    assert first['energy_threshold'] == second['energy_threshold']
    assert first['test_ids'] == []
    assert first['calibration_class_support'] == [3]
    assert not first['full_rejection_calibrated']


def test_calibration_rejects_overlapping_groups_identical_pose_and_unlabeled_truth():
    arrays = fixture()
    train = np.arange(7)<5
    cal = np.arange(7)==5
    z = np.zeros((7,5))
    arrays['group'][5] = 'train'
    with pytest.raises(ValueError,match='组泄漏'):
        partial_calibration(z,arrays,train,cal,1.)
    arrays['group'][5] = 'cal'
    arrays['raw17'][5] = arrays['raw17'][0]
    with pytest.raises(ValueError,match='姿态泄漏'):
        partial_calibration(z,arrays,train,cal,1.)
    with pytest.raises(ValueError,match='已知人工标签'):
        partial_calibration(z,arrays,train,np.arange(7)==6,1.)


def test_centroid_scaling_and_centers_do_not_use_calibration_or_test_rows():
    arrays = fixture()
    train = np.arange(7)<5
    x = arrays['raw17'].copy()
    first = centroid_logits(x,arrays['y5'],train)
    x[~train] = -10000
    second = centroid_logits(x,arrays['y5'],train)
    np.testing.assert_array_equal(first[train],second[train])
    assert first[train].argmax(1).tolist() == [0,1,2,3,4]
    with pytest.raises(ValueError,match='覆盖五类'):
        centroid_logits(x,arrays['y5'],np.arange(7)<4)


def test_feature_collision_does_not_relabel_or_claim_raw_pose_conflict():
    arrays = fixture()
    arrays['x51'] = arrays['raw17'].copy()
    arrays['x59'] = arrays['raw17'].copy()
    arrays['x51'][1] = arrays['x51'][0]
    arrays['x51'][6] = arrays['x51'][0]
    before = arrays['label_original'].copy()
    conflicts = feature_conflicts(arrays)
    assert conflicts['raw17'] == [] and conflicts['x59'] == []
    assert conflicts['x51'] == [{'ids':['a','b'],'pages':[1,2],'labels':['neutral','happy']}]
    np.testing.assert_array_equal(arrays['label_original'],before)
