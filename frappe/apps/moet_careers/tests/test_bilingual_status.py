from moet_careers.portal_utils import review_status as status


def test_fresh_suggestion_waits_for_review():
    assert status(False, False, True, None, "h1") == ("Suggested", "h1")


def test_unchanged_suggestion_stays_in_review():
    assert status(False, False, False, "h1", "h1") == ("Suggested", "h1")


def test_ticking_approves():
    assert status(True, False, False, "h1", "h1")[0] == "Approved"


def test_editing_a_suggestion_approves():
    assert status(False, False, False, "h1", "h2")[0] == "Approved"


def test_writing_it_yourself_approves():
    assert status(False, False, False, None, "h2")[0] == "Approved"


def test_unticking_puts_it_back_in_review():
    assert status(False, True, False, None, "h3") == ("Suggested", "h3")
    assert status(False, False, False, "h3", "h3")[0] == "Suggested"
