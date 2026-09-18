# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Synthetic Android evidence stays private throughout review, signing and delivery."""

import unittest
from unittest.mock import patch

import prepare_signed_review
import test_prepare_signed_review as signing_tests
import test_prepare_delivery as delivery_tests
import revalidate_delivery
from package_help import write_json
from build_help import sha


class AndroidReviewChainTests(unittest.TestCase):
    def test_changed_review_audit_cannot_be_signed(self):
        fixture = signing_tests.SignedReviewStageTests()
        fixture.setUp(android=True)
        self.addCleanup(fixture.doCleanups)
        for name in ("android-audit.json", "android-pins.json"):
            path = fixture.review.output / name
            original = path.read_bytes()
            try:
                path.write_bytes(original + b"changed")
                with patch.object(prepare_signed_review.signing, "sign") as sign:
                    with self.assertRaisesRegex(ValueError, "Android audit changed"):
                        fixture.run_stage()
                    sign.assert_not_called()
            finally:
                path.write_bytes(original)
        self.assertFalse(fixture.output.exists())

    def test_audit_chain_survives_signing_delivery_and_fresh_revalidation_without_being_sent(self):
        fixture = delivery_tests.DeliveryStageTests()
        fixture.setUp(android=True)
        self.addCleanup(fixture.doCleanups)
        result = fixture.run_stage()
        names = ("android-audit.json", "android-pins.json")
        for name in names:
            expected = (fixture.signed.review.output / name).read_bytes()
            self.assertEqual(expected, (fixture.signed.output / name).read_bytes())
            self.assertEqual(expected, (fixture.output / name).read_bytes())
            self.assertFalse((fixture.output / "delivery" / name).exists())
        self.assertEqual(len(list((fixture.output / "delivery").iterdir())), 2)
        settings = fixture.root / "revalidate-android-settings.json"
        output = fixture.root / "revalidated-android"
        write_json(settings, {"schemaVersion": 1, "preparedDirectory": str(fixture.output),
                              "preparationSettings": str(fixture.settings_path), "output": str(output)})
        validated = revalidate_delivery.revalidate(settings,
            sha((fixture.output / "delivery-review-receipt.json").read_bytes()), fixture.pin, fixture.authorization_pin)
        self.assertFalse(result["deliveryAttempted"])
        self.assertFalse(validated["deliveryAttempted"])
        for name in names:
            self.assertEqual((fixture.output / name).read_bytes(), (output / name).read_bytes())
            self.assertFalse((output / "delivery" / name).exists())
        changed_settings = fixture.root / "revalidate-changed-android.json"
        write_json(changed_settings, {"schemaVersion": 1, "preparedDirectory": str(fixture.output),
            "preparationSettings": str(fixture.settings_path), "output": str(fixture.root / "rejected-android")})
        (fixture.output / "android-audit.json").write_bytes(b"changed after preparation")
        with self.assertRaisesRegex(ValueError, "Android audit changed"):
            revalidate_delivery.revalidate(changed_settings,
                sha((fixture.output / "delivery-review-receipt.json").read_bytes()), fixture.pin, fixture.authorization_pin)

    def test_missing_or_changed_signed_audit_prevents_delivery_plan(self):
        fixture = delivery_tests.DeliveryStageTests()
        fixture.setUp(android=True)
        self.addCleanup(fixture.doCleanups)
        for folder in (fixture.signed.output, fixture.signed.review.output):
            path = folder / "android-audit.json"
            original = path.read_bytes()
            try:
                path.unlink()
                with self.assertRaises(OSError):
                    fixture.run_stage()
                path.write_bytes(b"changed")
                with self.assertRaisesRegex(ValueError, "Android audit changed"):
                    fixture.run_stage()
            finally:
                path.write_bytes(original)
        self.assertFalse(fixture.output.exists())


if __name__ == "__main__":
    unittest.main()
