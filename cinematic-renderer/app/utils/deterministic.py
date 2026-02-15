import hashlib
from random import Random

import numpy as np


def stable_seed(seed: int, namespace: str) -> int:
    digest = hashlib.sha256(f"{seed}:{namespace}".encode("utf-8")).hexdigest()
    return int(digest[:8], 16)


def rng_for(seed: int, namespace: str) -> Random:
    return Random(stable_seed(seed, namespace))


def np_rng_for(seed: int, namespace: str) -> np.random.Generator:
    return np.random.default_rng(stable_seed(seed, namespace))
