//! Tiny seeded PRNG (xorshift64*), so every fuzz run is reproducible from its
//! `--seed`. Deliberately dependency-free and not cryptographic - we only need a
//! deterministic, well-spread sequence.

pub struct Rng {
    state: u64,
}

impl Rng {
    pub fn new(seed: u64) -> Self {
        // XOR with a nonzero constant so distinct seeds stay distinct; only the
        // all-zero state (which xorshift can't leave) needs special-casing.
        let mut state = seed ^ 0x9E37_79B9_7F4A_7C15;
        if state == 0 {
            state = 0x9E37_79B9_7F4A_7C15;
        }
        Rng { state }
    }

    pub fn next_u64(&mut self) -> u64 {
        let mut x = self.state;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        self.state = x;
        x.wrapping_mul(0x2545_F491_4F6C_DD1D)
    }

    /// Uniform-ish value in `0..n` (0 when `n == 0`).
    pub fn below(&mut self, n: usize) -> usize {
        if n == 0 {
            0
        } else {
            (self.next_u64() % n as u64) as usize
        }
    }

    pub fn bool(&mut self) -> bool {
        self.next_u64() & 1 == 1
    }

    /// Pick a reference to a random element (panics on empty slice, like index).
    pub fn choice<'a, T>(&mut self, items: &'a [T]) -> &'a T {
        &items[self.below(items.len())]
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn deterministic_for_a_seed() {
        let a: Vec<u64> = {
            let mut r = Rng::new(42);
            (0..8).map(|_| r.next_u64()).collect()
        };
        let b: Vec<u64> = {
            let mut r = Rng::new(42);
            (0..8).map(|_| r.next_u64()).collect()
        };
        assert_eq!(a, b);
        // A different seed diverges.
        let mut r = Rng::new(43);
        let c: Vec<u64> = (0..8).map(|_| r.next_u64()).collect();
        assert_ne!(a, c);
    }

    #[test]
    fn below_stays_in_range() {
        let mut r = Rng::new(1);
        for _ in 0..1000 {
            assert!(r.below(10) < 10);
        }
        assert_eq!(r.below(0), 0);
    }
}
