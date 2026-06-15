// Accessibility remediations for the Material for MkDocs theme.
//
// The stock theme ships a few controls without accessible names, and wraps the
// search field in a way that some screen readers (notably VoiceOver) do not
// surface well. This patches them at runtime so they announce and navigate
// correctly. Kept as one file on purpose: it survives theme upgrades without
// maintaining copies of the theme's templates.
(function () {
  function patch() {
    // 1. The header's search toggle is a <label> containing only an icon, with
    //    no accessible name, so it reads as an unlabeled "clickable".
    document
      .querySelectorAll('label.md-header__button[for="__search"]')
      .forEach(function (el) {
        el.setAttribute("aria-label", "Search");
        el.setAttribute("title", "Search");
      });

    // 2. Expose the search field as a real edit field.
    //    The search container ships as role="dialog" with no name. On desktop
    //    the search is an inline text field, not a modal, and the dialog role
    //    pushes the field out of the normal browse-mode flow, so single-letter
    //    / quick navigation (for example "e" for the next edit field) cannot
    //    land on it. Dropping the dialog role lets the field sit in the page as
    //    an ordinary edit field. It already carries type="text" and
    //    aria-label="Search", so it then announces as a named search field.
    var search = document.querySelector('.md-search[role="dialog"]');
    if (search) {
      search.removeAttribute("role");
    }
    //    Name the inner search landmark so it still announces as "Search".
    var inner = document.querySelector(".md-search__inner");
    if (inner && !inner.getAttribute("aria-label")) {
      inner.setAttribute("aria-label", "Search");
    }

    // 3. The light/dark palette toggles. Each radio input already carries its
    //    own aria-label ("Switch to dark mode" / "Switch to light mode"), but
    //    each is also tied to a hidden, icon-only <label>. VoiceOver can take
    //    the control's name from that associated label, which computes to an
    //    empty string, and then fail to fall back to the aria-label, so one
    //    toggle goes silent. Removing the decorative labels from the
    //    accessibility tree lets each radio's own aria-label be announced.
    document
      .querySelectorAll('form[data-md-component="palette"] > label')
      .forEach(function (label) {
        label.setAttribute("aria-hidden", "true");
      });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", patch);
  } else {
    patch();
  }
})();
