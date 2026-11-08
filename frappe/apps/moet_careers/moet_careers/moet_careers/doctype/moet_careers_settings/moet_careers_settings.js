frappe.ui.form.on("MoET Careers Settings", {
	suggest_portal_text(frm) {
		const language = frm.doc.suggest_language;
		if (!language) {
			frappe.msgprint(__("Choose the language first."));
			return;
		}
		frappe.call({
			method: "moet_careers.i18n.suggest_portal_text",
			args: { language },
			freeze: true,
			callback: (r) => frappe.msgprint(r.message),
		});
	},
});
