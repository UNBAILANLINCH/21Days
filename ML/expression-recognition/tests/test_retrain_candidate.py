"""微调护栏的数值fixture，不是人工或独立分类效果证据。"""
import numpy as np
import pytest
import torch

from analysis.laila_v2_candidate.retrain_candidate import expanded_model, train_mask, fit, score
from exprnet.canonical import load_canonical
from exprnet.models import ResMLP


def test_zero_extended_59_starts_identical_to_51():
    torch.manual_seed(42)
    baseline = ResMLP(load_canonical(), 5, width=16, hidden=24, blocks=1).eval()
    candidate = expanded_model(baseline, 59).eval()
    raw = np.random.default_rng(42).normal(size=(20,59)).astype(np.float32)
    with torch.no_grad():
        torch.testing.assert_close(candidate(torch.from_numpy(raw)), baseline(torch.from_numpy(raw[:,:51])), rtol=0, atol=1e-6)
    assert baseline.inp.in_features == 51
    assert torch.count_nonzero(candidate.inp.weight[:,51:]) == 0


def arrays():
    return {'y5': np.array([0,1,2,3,4,-1,3],dtype=np.int32),
            'supervised5_mask': np.array([1,1,1,1,1,0,1],dtype=bool),
            'group': np.array(['train']*6+['heldout']),
            'raw17': np.arange(119,dtype=np.float32).reshape(7,17),
            'label_original': np.array(['neutral','happy','sad','fear','angry','ambiguous','surprise']),
            'confused_candidate_mask': np.array([0,0,0,0,0,1,0],dtype=bool),
            'id': np.array([str(i) for i in range(7)])}


def test_whole_group_split_and_unknown_exclusion():
    data = arrays()
    assert train_mask(data,'train','heldout').tolist() == [True]*5+[False]*2
    assert train_mask(data).tolist() == [True]*5+[False,True]
    with pytest.raises(ValueError):
        train_mask(data,'train','train')
    data['raw17'][-1] = data['raw17'][0]
    with pytest.raises(ValueError,match='相同raw17'):
        train_mask(data,'train','heldout')


def test_missing_class_does_not_get_pseudo_labels():
    data = arrays()
    baseline = ResMLP(load_canonical(),5,width=8,hidden=8,blocks=1).eval()
    x = np.zeros((7,51),dtype=np.float32)
    mask = train_mask(data,'train','heldout')
    mask[4] = False
    with pytest.raises(ValueError,match='缺五类'):
        fit(baseline,x,data['y5'],mask,epochs=2)
    with pytest.raises(ValueError,match='预算'):
        fit(baseline,x,data['y5'],mask,epochs=101)


def test_same_seed_fixed_budget_repeatable_and_baseline_immutable():
    torch.set_num_threads(2)
    torch.manual_seed(42)
    baseline = ResMLP(load_canonical(),5,width=8,hidden=8,blocks=1).eval()
    before = {key:value.clone() for key,value in baseline.state_dict().items()}
    data = arrays()
    x = np.random.default_rng(42).random((7,59)).astype(np.float32)
    mask = train_mask(data,'train','heldout')
    first, _ = fit(baseline,x,data['y5'],mask,epochs=2)
    second, _ = fit(baseline,x,data['y5'],mask,epochs=2)
    for key,value in first.state_dict().items():
        torch.testing.assert_close(value,second.state_dict()[key],rtol=0,atol=0)
    for key,value in before.items():
        torch.testing.assert_close(value,baseline.state_dict()[key],rtol=0,atol=0)


def test_wrong_acceptance_is_distinct_from_unknown_output():
    data = arrays()
    z = np.zeros((7,5),dtype=np.float32)
    z[:,0] = 4
    result = score(z,data,temperature=1,threshold=0,min_confidence=.4)
    assert result['known_count'] == 6
    assert result['argmax_agree'] == 1
    assert result['known_wrong_accepted'] == 5
    assert result['excluded_output_only']['ambiguous']['count'] == 1
    assert result['confused_output_only']['count'] == 1
    rejected = score(z,data,temperature=1,threshold=-100,min_confidence=.4)
    assert rejected['known_rejected'] == 6 and rejected['known_wrong_accepted'] == 0
