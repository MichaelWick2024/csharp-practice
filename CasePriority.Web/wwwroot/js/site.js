// Client-side enhancement only: filters rows already delivered to the browser.
// It performs no protected mutations and does not bypass API validation.
$(function () {
    $("#case-filter").on("input", function () {
        const query = $(this).val().toString().toLowerCase();

        $("#case-table tbody tr").each(function () {
            const text = $(this).text().toLowerCase();
            $(this).toggle(text.includes(query));
        });
    });
});
