// noinspection JSUnresolvedReference
const fs = require('fs');
const content = fs.readFileSync('wwwroot/lib/jqtree/tree.jquery.js', 'utf8');
if (content.includes('checkbox')) {
    console.log("Checkboxes are supported");
} else {
    console.log("Checkboxes not found");
}
