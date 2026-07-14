/**
 * Shared cross-page navigation for the Northwind showcase (Requirement 7.1).
 *
 * The showcase is three static pages served by one host — Simple Wiring, View Browser, and Custom
 * Renderer (design D137/D139). Rather than duplicate the markup on every page, each page imports this
 * module and calls {@link renderNav} to inject the same nav bar, passing its own page id so the current
 * page is highlighted.
 *
 * This module is deliberately dependency-free: it builds plain DOM nodes and never touches jQuery, AG
 * Grid, or any CDN global. That keeps it trivially loadable as an ES module on every page (including the
 * AG Grid pages, which do not load jQuery) and keeps `tsc --noEmit` green with no extra ambient types.
 *
 * The {@link NAV_ITEMS} list is the single source of truth for the page set and their file names; the
 * three page HTML files link to each other through exactly these `href`s.
 */
/**
 * The showcase page set, in navigation order. This is the single source of truth for the three pages'
 * file names — the page HTML files must be served under these `href`s so the shared nav links resolve.
 */
export const NAV_ITEMS = [
    { id: 'simple-wiring', label: 'Simple Wiring', href: 'index.html' },
    { id: 'view-browser', label: 'View Browser', href: 'view-browser.html' },
    { id: 'custom-renderer', label: 'Custom Renderer', href: 'custom-renderer.html' },
];
/**
 * Renders the shared navigation into `container`, highlighting the link for `activePage`.
 *
 * The active link is marked both semantically (`aria-current="page"`, for assistive technology) and
 * visually (a `nav-link--active` CSS class the page's stylesheet can target). Any existing children of
 * `container` are replaced, so calling this more than once is safe and idempotent.
 *
 * @param activePage The id of the page currently being viewed; its link is marked active.
 * @param container  The element to render the nav into (typically a `<div id="nav">` header slot).
 */
export function renderNav(activePage, container) {
    const nav = document.createElement('nav');
    nav.className = 'showcase-nav';
    nav.setAttribute('aria-label', 'Showcase pages');
    const list = document.createElement('ul');
    list.className = 'nav-list';
    for (const item of NAV_ITEMS) {
        const li = document.createElement('li');
        li.className = 'nav-item';
        const link = document.createElement('a');
        link.className = 'nav-link';
        link.href = item.href;
        link.textContent = item.label;
        if (item.id === activePage) {
            link.classList.add('nav-link--active');
            link.setAttribute('aria-current', 'page');
        }
        li.appendChild(link);
        list.appendChild(li);
    }
    nav.appendChild(list);
    container.replaceChildren(nav);
}
//# sourceMappingURL=nav.js.map